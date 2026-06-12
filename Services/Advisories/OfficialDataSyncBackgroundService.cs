using RagAgentConsole.Models;
using Microsoft.Extensions.Options;

namespace RagAgentConsole.Services;

/// <summary>
/// 定期把官方比賽與新聞資料更新進本機資料庫。
/// </summary>
public class OfficialDataSyncBackgroundService(
    IServiceScopeFactory scopeFactory,
    IOptions<DataSourceOptions> dataSourceOptions,
    ILogger<OfficialDataSyncBackgroundService> logger) : BackgroundService
{
    // 單實例保證交給部署層（worker 單副本 / CronJob），這裡只做單純的定時迴圈。
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // 先讓 web app 啟動穩定，再開始背景同步。
        await DelayAsync(GetStartupDelay(), stoppingToken);

        while (!stoppingToken.IsCancellationRequested)
        {
            var intervalMinutes = Math.Max(5, dataSourceOptions.Value.AutoSyncIntervalMinutes);

            try
            {
                using var scope = scopeFactory.CreateScope();
                await ExecuteOwnedWorkAsync(scope.ServiceProvider, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception exception)
            {
                logger.LogWarning(exception, "Official data auto sync failed. Will retry on the next interval.");
            }

            await DelayAsync(TimeSpan.FromMinutes(intervalMinutes), stoppingToken);
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

    protected virtual TimeSpan GetStartupDelay() => TimeSpan.FromSeconds(3);

    protected virtual Task ExecuteOwnedWorkAsync(IServiceProvider serviceProvider, CancellationToken cancellationToken)
        => ExecuteSyncOnceAsync(serviceProvider, cancellationToken);

    protected virtual Task DelayAsync(TimeSpan delay, CancellationToken cancellationToken)
        => Task.Delay(delay, cancellationToken);
}
