using CPBLLineBotCloud.Models;
using Microsoft.Extensions.Options;

namespace CPBLLineBotCloud.Services;

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
        var gameSyncService = serviceProvider.GetRequiredService<ICpblGameSyncService>();
        var newsSyncService = serviceProvider.GetRequiredService<IBaseballNewsSyncService>();
        var logger = serviceProvider.GetRequiredService<ILogger<OfficialDataSyncBackgroundService>>();
        var today = DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeBySystemTimeZoneId(DateTime.UtcNow, "Taipei Standard Time"));

        var gameCount = 0;
        // 往前補抓幾天，延後結算或官方晚修正的資料比較不容易漏掉。
        for (var dayOffset = -4; dayOffset <= 1; dayOffset++)
        {
            gameCount += await gameSyncService.SyncDateAsync(today.AddDays(dayOffset), cancellationToken);
        }

        var newsCount = await newsSyncService.SyncAsync(cancellationToken);

        logger.LogInformation(
            "Official data auto sync completed. Games updated: {GameCount}. News added: {NewsCount}.",
            gameCount,
            newsCount);
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
