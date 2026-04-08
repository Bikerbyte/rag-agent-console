using CPBLLineBotCloud.Data;
using CPBLLineBotCloud.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace CPBLLineBotCloud.Services;

/// <summary>
/// 用資料庫租約控制 scheduled jobs 的單活執行，避免多台節點重複跑同步或通知工作。
/// </summary>
public class RuntimeLeadershipLeaseService(
    ApplicationDbContext dbContext,
    TimeProvider timeProvider,
    IOptions<AppRuntimeOptions> runtimeOptions,
    ILogger<RuntimeLeadershipLeaseService> logger) : IRuntimeLeadershipLeaseService
{
    public async Task<RuntimeLeadershipLeaseResult> TryAcquireOrRenewAsync(string leaseName, CancellationToken cancellationToken = default)
    {
        var normalizedLeaseName = leaseName.Trim();
        var instanceName = runtimeOptions.Value.GetEffectiveInstanceName();
        var now = timeProvider.GetUtcNow();
        var expiresAt = now.Add(runtimeOptions.Value.GetLeadershipLeaseDuration());

        for (var attempt = 0; attempt < 3; attempt++)
        {
            var lease = await dbContext.RuntimeLeadershipLeases
                .AsNoTracking()
                .SingleOrDefaultAsync(item => item.LeaseName == normalizedLeaseName, cancellationToken);

            if (lease is null)
            {
                dbContext.RuntimeLeadershipLeases.Add(new RuntimeLeadershipLease
                {
                    LeaseName = normalizedLeaseName,
                    OwnerInstanceName = instanceName,
                    AcquiredAt = now,
                    RenewedAt = now,
                    ExpiresAt = expiresAt,
                    LastUpdatedAt = now
                });

                try
                {
                    await dbContext.SaveChangesAsync(cancellationToken);
                    logger.LogInformation(
                        "Leadership lease acquired. Lease: {LeaseName}. Instance: {InstanceName}. ExpiresAt: {ExpiresAt}.",
                        normalizedLeaseName,
                        instanceName,
                        expiresAt);

                    return BuildResult(normalizedLeaseName, instanceName, instanceName, expiresAt, true, "Acquired");
                }
                catch (DbUpdateException)
                {
                    dbContext.ChangeTracker.Clear();
                    continue;
                }
            }

            if (string.Equals(lease.OwnerInstanceName, instanceName, StringComparison.Ordinal))
            {
                var affectedRows = await RenewOwnedLeaseAsync(normalizedLeaseName, instanceName, now, expiresAt, cancellationToken);

                if (affectedRows == 1)
                {
                    logger.LogDebug(
                        "Leadership lease renewed. Lease: {LeaseName}. Instance: {InstanceName}. ExpiresAt: {ExpiresAt}.",
                        normalizedLeaseName,
                        instanceName,
                        expiresAt);

                    return BuildResult(normalizedLeaseName, instanceName, instanceName, expiresAt, true, "Renewed");
                }

                dbContext.ChangeTracker.Clear();
                continue;
            }

            if (lease.ExpiresAt <= now)
            {
                var previousOwner = lease.OwnerInstanceName;
                var previousExpiresAt = lease.ExpiresAt;

                var affectedRows = await TakeOverExpiredLeaseAsync(
                    normalizedLeaseName,
                    previousOwner,
                    previousExpiresAt,
                    instanceName,
                    now,
                    expiresAt,
                    cancellationToken);

                if (affectedRows == 1)
                {
                    logger.LogInformation(
                        "Leadership lease takeover succeeded. Lease: {LeaseName}. Instance: {InstanceName}. PreviousOwner: {PreviousOwner}. ExpiresAt: {ExpiresAt}.",
                        normalizedLeaseName,
                        instanceName,
                        previousOwner,
                        expiresAt);

                    return BuildResult(normalizedLeaseName, instanceName, instanceName, expiresAt, true, "TakenOver");
                }

                dbContext.ChangeTracker.Clear();
                continue;
            }

            logger.LogDebug(
                "Leadership lease acquire skipped because another node is active. Lease: {LeaseName}. Instance: {InstanceName}. CurrentOwner: {CurrentOwner}. ExpiresAt: {ExpiresAt}.",
                normalizedLeaseName,
                instanceName,
                lease.OwnerInstanceName,
                lease.ExpiresAt);

            return BuildResult(normalizedLeaseName, instanceName, lease.OwnerInstanceName, lease.ExpiresAt, false, "Rejected");
        }

        var currentLease = await dbContext.RuntimeLeadershipLeases
            .AsNoTracking()
            .SingleOrDefaultAsync(item => item.LeaseName == normalizedLeaseName, cancellationToken);

        return BuildResult(
            normalizedLeaseName,
            instanceName,
            currentLease?.OwnerInstanceName ?? string.Empty,
            currentLease?.ExpiresAt ?? now,
            currentLease is not null && string.Equals(currentLease.OwnerInstanceName, instanceName, StringComparison.Ordinal) && currentLease.ExpiresAt > now,
            "Unknown");
    }

    public async Task<bool> IsCurrentLeaderAsync(string leaseName, CancellationToken cancellationToken = default)
    {
        var instanceName = runtimeOptions.Value.GetEffectiveInstanceName();
        var now = timeProvider.GetUtcNow();

        return await dbContext.RuntimeLeadershipLeases.AnyAsync(
            item =>
                item.LeaseName == leaseName &&
                item.OwnerInstanceName == instanceName &&
                item.ExpiresAt > now,
            cancellationToken);
    }

    public async Task ReleaseIfOwnedAsync(string leaseName, CancellationToken cancellationToken = default)
    {
        var instanceName = runtimeOptions.Value.GetEffectiveInstanceName();
        var now = timeProvider.GetUtcNow();

        var affectedRows = await ReleaseOwnedLeaseAsync(leaseName, instanceName, now, cancellationToken);

        if (affectedRows == 1)
        {
            logger.LogInformation(
                "Leadership lease released by owner. Lease: {LeaseName}. Instance: {InstanceName}.",
                leaseName,
                instanceName);
        }
    }

    public async Task<IReadOnlyList<RuntimeLeadershipLease>> GetActiveLeasesAsync(CancellationToken cancellationToken = default)
    {
        var now = timeProvider.GetUtcNow();

        return await dbContext.RuntimeLeadershipLeases
            .AsNoTracking()
            .Where(item => item.ExpiresAt > now)
            .OrderBy(item => item.LeaseName)
            .ToListAsync(cancellationToken);
    }

    private static RuntimeLeadershipLeaseResult BuildResult(
        string leaseName,
        string instanceName,
        string currentOwnerInstanceName,
        DateTimeOffset expiresAt,
        bool isLeader,
        string action)
    {
        return new RuntimeLeadershipLeaseResult
        {
            LeaseName = leaseName,
            InstanceName = instanceName,
            CurrentOwnerInstanceName = currentOwnerInstanceName,
            ExpiresAt = expiresAt,
            IsLeader = isLeader,
            Action = action
        };
    }

    private async Task<int> RenewOwnedLeaseAsync(
        string leaseName,
        string instanceName,
        DateTimeOffset now,
        DateTimeOffset expiresAt,
        CancellationToken cancellationToken)
    {
        if (dbContext.Database.IsRelational())
        {
            return await dbContext.RuntimeLeadershipLeases
                .Where(item => item.LeaseName == leaseName && item.OwnerInstanceName == instanceName)
                .ExecuteUpdateAsync(setters => setters
                    .SetProperty(item => item.RenewedAt, now)
                    .SetProperty(item => item.ExpiresAt, expiresAt)
                    .SetProperty(item => item.LastUpdatedAt, now),
                    cancellationToken);
        }

        var lease = await dbContext.RuntimeLeadershipLeases
            .SingleOrDefaultAsync(item => item.LeaseName == leaseName && item.OwnerInstanceName == instanceName, cancellationToken);

        if (lease is null)
        {
            return 0;
        }

        lease.RenewedAt = now;
        lease.ExpiresAt = expiresAt;
        lease.LastUpdatedAt = now;
        return await dbContext.SaveChangesAsync(cancellationToken);
    }

    private async Task<int> TakeOverExpiredLeaseAsync(
        string leaseName,
        string previousOwner,
        DateTimeOffset previousExpiresAt,
        string nextOwner,
        DateTimeOffset now,
        DateTimeOffset expiresAt,
        CancellationToken cancellationToken)
    {
        if (dbContext.Database.IsRelational())
        {
            return await dbContext.RuntimeLeadershipLeases
                .Where(item =>
                    item.LeaseName == leaseName &&
                    item.OwnerInstanceName == previousOwner &&
                    item.ExpiresAt == previousExpiresAt)
                .ExecuteUpdateAsync(setters => setters
                    .SetProperty(item => item.OwnerInstanceName, nextOwner)
                    .SetProperty(item => item.AcquiredAt, now)
                    .SetProperty(item => item.RenewedAt, now)
                    .SetProperty(item => item.ExpiresAt, expiresAt)
                    .SetProperty(item => item.LastUpdatedAt, now),
                    cancellationToken);
        }

        var lease = await dbContext.RuntimeLeadershipLeases
            .SingleOrDefaultAsync(item =>
                item.LeaseName == leaseName &&
                item.OwnerInstanceName == previousOwner &&
                item.ExpiresAt == previousExpiresAt,
                cancellationToken);

        if (lease is null)
        {
            return 0;
        }

        lease.OwnerInstanceName = nextOwner;
        lease.AcquiredAt = now;
        lease.RenewedAt = now;
        lease.ExpiresAt = expiresAt;
        lease.LastUpdatedAt = now;
        return await dbContext.SaveChangesAsync(cancellationToken);
    }

    private async Task<int> ReleaseOwnedLeaseAsync(
        string leaseName,
        string instanceName,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        if (dbContext.Database.IsRelational())
        {
            return await dbContext.RuntimeLeadershipLeases
                .Where(item => item.LeaseName == leaseName && item.OwnerInstanceName == instanceName)
                .ExecuteUpdateAsync(setters => setters
                    .SetProperty(item => item.ExpiresAt, now)
                    .SetProperty(item => item.LastUpdatedAt, now),
                    cancellationToken);
        }

        var lease = await dbContext.RuntimeLeadershipLeases
            .SingleOrDefaultAsync(item => item.LeaseName == leaseName && item.OwnerInstanceName == instanceName, cancellationToken);

        if (lease is null)
        {
            return 0;
        }

        lease.ExpiresAt = now;
        lease.LastUpdatedAt = now;
        return await dbContext.SaveChangesAsync(cancellationToken);
    }
}
