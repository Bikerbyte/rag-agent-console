using CPBLLineBotCloud.Models;
using Microsoft.Extensions.Options;

namespace CPBLLineBotCloud.Services;

/// <summary>
/// 定期執行 Telegram 排程推播判斷的背景工作迴圈。
/// </summary>
public class TelegramNotificationBackgroundService(
    IServiceScopeFactory scopeFactory,
    TimeProvider timeProvider,
    IOptions<AppRuntimeOptions> runtimeOptions,
    IOptions<PushNotificationOptions> pushOptions,
    ILogger<TelegramNotificationBackgroundService> logger) : BackgroundService
{
    private const string LeaseName = "TelegramNotification";

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // 稍微等一下再跑，讓 migration 和 seed data 先完成。
        await DelayAsync(GetStartupDelay(), stoppingToken);
        var nextRunAt = timeProvider.GetUtcNow();

        while (!stoppingToken.IsCancellationRequested)
        {
            var intervalSeconds = Math.Max(30, pushOptions.Value.WorkerIntervalSeconds);
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
                            "Telegram notification dispatch skipped because current node is not the active leader. Lease: {LeaseName}. Instance: {InstanceName}. CurrentOwner: {CurrentOwner}. ExpiresAt: {ExpiresAt}.",
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
                    nextRunAt = timeProvider.GetUtcNow().AddSeconds(intervalSeconds);
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception exception)
            {
                logger.LogWarning(exception, "Telegram notification dispatch failed. Will retry on the next interval.");
                nextRunAt = timeProvider.GetUtcNow().AddSeconds(intervalSeconds);
            }

            var delay = BuildDelay(
                timeProvider.GetUtcNow(),
                nextRunAt,
                currentRuntimeOptions.EnableLeadershipLease
                    ? currentRuntimeOptions.GetLeadershipLeaseRenewInterval()
                    : TimeSpan.FromSeconds(intervalSeconds));

            await DelayAsync(delay, stoppingToken);
        }

        if (runtimeOptions.Value.EnableLeadershipLease)
        {
            using var scope = scopeFactory.CreateScope();
            var leaseService = scope.ServiceProvider.GetRequiredService<IRuntimeLeadershipLeaseService>();
            await leaseService.ReleaseIfOwnedAsync(LeaseName, CancellationToken.None);
        }
    }

    internal static async Task ExecuteDispatchOnceAsync(IServiceProvider serviceProvider, CancellationToken cancellationToken)
    {
        var dispatchService = serviceProvider.GetRequiredService<ITelegramNotificationDispatchService>();
        await dispatchService.ProcessPendingNotificationsAsync(cancellationToken);
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

    protected virtual TimeSpan GetStartupDelay() => TimeSpan.FromSeconds(10);

    protected virtual Task ExecuteOwnedWorkAsync(IServiceProvider serviceProvider, CancellationToken cancellationToken)
        => ExecuteDispatchOnceAsync(serviceProvider, cancellationToken);

    protected virtual Task DelayAsync(TimeSpan delay, CancellationToken cancellationToken)
        => Task.Delay(delay, cancellationToken);
}
