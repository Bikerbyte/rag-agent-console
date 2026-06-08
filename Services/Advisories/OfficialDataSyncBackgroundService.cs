using SecurityAdvisoryBot.Models;
using Microsoft.Extensions.Options;

namespace SecurityAdvisoryBot.Services;

/// <summary>
/// 定期把官方比賽與新聞資料更新進本機資料庫。
/// </summary>
public class OfficialDataSyncBackgroundService(
    IServiceScopeFactory scopeFactory,
    TimeProvider timeProvider,
    IOptions<AppRuntimeOptions> runtimeOptions,
    IOptions<DataSourceOptions> dataSourceOptions,
    ILogger<OfficialDataSyncBackgroundService> logger) : BackgroundService
{
    private const string LeaseName = "OfficialDataSync";

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // 先讓 web app 啟動穩定，再開始背景同步。
        await DelayAsync(GetStartupDelay(), stoppingToken);
        var nextRunAt = timeProvider.GetUtcNow();

        while (!stoppingToken.IsCancellationRequested)
        {
            var intervalMinutes = Math.Max(5, dataSourceOptions.Value.AutoSyncIntervalMinutes);
            var currentRuntimeOptions = runtimeOptions.Value;
            var now = timeProvider.GetUtcNow();
            var shouldExecute = now >= nextRunAt;

            try
            {
                using var scope = scopeFactory.CreateScope();
                if (currentRuntimeOptions.EnableLeadershipLease)
                {
                    var leaseService = scope.ServiceProvider.GetRequiredService<IRuntimeLeadershipLeaseService>();
                    var leaseResult = await leaseService.TryAcquireOrRenewAsync(LeaseName, stoppingToken);

                    if (!leaseResult.IsLeader)
                    {
                        logger.LogDebug(
                            "Official data sync skipped because current node is not the active leader. Lease: {LeaseName}. Instance: {InstanceName}. CurrentOwner: {CurrentOwner}. ExpiresAt: {ExpiresAt}.",
                            LeaseName,
                            leaseResult.InstanceName,
                            leaseResult.CurrentOwnerInstanceName,
                            leaseResult.ExpiresAt);

                        await DelayAsync(currentRuntimeOptions.GetLeadershipLeaseAcquireRetryInterval(), stoppingToken);
                        continue;
                    }
                }

                if (shouldExecute)
                {
                    await ExecuteOwnedWorkAsync(scope.ServiceProvider, stoppingToken);
                    nextRunAt = timeProvider.GetUtcNow().AddMinutes(intervalMinutes);
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception exception)
            {
                logger.LogWarning(exception, "Official data auto sync failed. Will retry on the next interval.");
                nextRunAt = timeProvider.GetUtcNow().AddMinutes(intervalMinutes);
            }

            var delay = BuildDelay(
                timeProvider.GetUtcNow(),
                nextRunAt,
                currentRuntimeOptions.EnableLeadershipLease
                    ? currentRuntimeOptions.GetLeadershipLeaseRenewInterval()
                    : TimeSpan.FromMinutes(intervalMinutes));

            await DelayAsync(delay, stoppingToken);
        }

        if (runtimeOptions.Value.EnableLeadershipLease)
        {
            using var scope = scopeFactory.CreateScope();
            var leaseService = scope.ServiceProvider.GetRequiredService<IRuntimeLeadershipLeaseService>();
            await leaseService.ReleaseIfOwnedAsync(LeaseName, CancellationToken.None);
        }
    }

    internal static async Task ExecuteSyncOnceAsync(IServiceProvider serviceProvider, CancellationToken cancellationToken)
    {
        var advisorySyncService = serviceProvider.GetRequiredService<ISecurityAdvisorySyncService>();
        var logger = serviceProvider.GetRequiredService<ILogger<OfficialDataSyncBackgroundService>>();
        var result = await advisorySyncService.SyncAsync(cancellationToken);

        logger.LogInformation(
            "Sample connector auto sync completed. Sources: {SourceCount}. Fetched: {FetchedCount}. Added: {AddedCount}. Updated: {UpdatedCount}. Chunks: {ChunkCount}.",
            result.SourceCount,
            result.FetchedCount,
            result.AddedCount,
            result.UpdatedCount,
            result.ChunkCount);
    }

    private static TimeSpan BuildDelay(DateTimeOffset now, DateTimeOffset nextRunAt, TimeSpan upperBoundDelay)
    {
        var remaining = nextRunAt - now;
        if (remaining <= TimeSpan.Zero)
        {
            return TimeSpan.FromSeconds(1);
        }

        return remaining < upperBoundDelay ? remaining : upperBoundDelay;
    }

    protected virtual TimeSpan GetStartupDelay() => TimeSpan.FromSeconds(3);

    protected virtual Task ExecuteOwnedWorkAsync(IServiceProvider serviceProvider, CancellationToken cancellationToken)
        => ExecuteSyncOnceAsync(serviceProvider, cancellationToken);

    protected virtual Task DelayAsync(TimeSpan delay, CancellationToken cancellationToken)
        => Task.Delay(delay, cancellationToken);
}
