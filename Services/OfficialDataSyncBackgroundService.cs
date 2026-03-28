using CPBLLineBotCloud.Models;
using Microsoft.Extensions.Options;

namespace CPBLLineBotCloud.Services;

public class OfficialDataSyncBackgroundService(
    IServiceScopeFactory scopeFactory,
    IOptions<DataSourceOptions> dataSourceOptions,
    ILogger<OfficialDataSyncBackgroundService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
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
