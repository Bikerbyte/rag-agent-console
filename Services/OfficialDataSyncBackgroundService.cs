using CPBLLineBotCloud.Models;
using Microsoft.Extensions.Options;

namespace CPBLLineBotCloud.Services;

/// <summary>
/// 定期把官方比賽與新聞資料更新進本機資料庫。
/// </summary>
public class OfficialDataSyncBackgroundService(
    IServiceScopeFactory scopeFactory,
    IOptions<DataSourceOptions> dataSourceOptions,
    ILogger<OfficialDataSyncBackgroundService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // 先讓 web app 啟動穩定，再開始背景同步。
        await Task.Delay(TimeSpan.FromSeconds(3), stoppingToken);

        while (!stoppingToken.IsCancellationRequested)
        {
            var intervalMinutes = Math.Max(5, dataSourceOptions.Value.AutoSyncIntervalMinutes);

            try
            {
                using var scope = scopeFactory.CreateScope();
                var gameSyncService = scope.ServiceProvider.GetRequiredService<ICpblGameSyncService>();
                var newsSyncService = scope.ServiceProvider.GetRequiredService<IBaseballNewsSyncService>();
                var today = DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeBySystemTimeZoneId(DateTime.UtcNow, "Taipei Standard Time"));

                var gameCount = 0;
                // 往前補抓幾天，延後結算或官方晚修正的資料比較不容易漏掉。
                for (var dayOffset = -4; dayOffset <= 1; dayOffset++)
                {
                    gameCount += await gameSyncService.SyncDateAsync(today.AddDays(dayOffset), stoppingToken);
                }

                var newsCount = await newsSyncService.SyncAsync(stoppingToken);

                logger.LogInformation(
                    "Official data auto sync completed. Games updated: {GameCount}. News added: {NewsCount}.",
                    gameCount,
                    newsCount);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception exception)
            {
                logger.LogWarning(exception, "Official data auto sync failed. Will retry on the next interval.");
            }

            await Task.Delay(TimeSpan.FromMinutes(intervalMinutes), stoppingToken);
        }
    }
}
