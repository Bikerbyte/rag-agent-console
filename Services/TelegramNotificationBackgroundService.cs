using CPBLLineBotCloud.Models;
using Microsoft.Extensions.Options;

namespace CPBLLineBotCloud.Services;

/// <summary>
/// 定期執行 Telegram 排程推播判斷的背景工作迴圈。
/// </summary>
public class TelegramNotificationBackgroundService(
    IServiceScopeFactory scopeFactory,
    IOptions<PushNotificationOptions> pushOptions,
    ILogger<TelegramNotificationBackgroundService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // 稍微等一下再跑，讓 migration 和 seed data 先完成。
        await Task.Delay(TimeSpan.FromSeconds(10), stoppingToken);

        while (!stoppingToken.IsCancellationRequested)
        {
            var intervalSeconds = Math.Max(30, pushOptions.Value.WorkerIntervalSeconds);

            try
            {
                using var scope = scopeFactory.CreateScope();
                var dispatchService = scope.ServiceProvider.GetRequiredService<ITelegramNotificationDispatchService>();
                await dispatchService.ProcessPendingNotificationsAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception exception)
            {
                logger.LogWarning(exception, "Telegram notification dispatch failed. Will retry on the next interval.");
            }

            await Task.Delay(TimeSpan.FromSeconds(intervalSeconds), stoppingToken);
        }
    }
}
