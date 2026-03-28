using CPBLLineBotCloud.Models;
using Microsoft.Extensions.Options;

namespace CPBLLineBotCloud.Services;

public class TelegramNotificationBackgroundService(
    IServiceScopeFactory scopeFactory,
    IOptions<PushNotificationOptions> pushOptions,
    ILogger<TelegramNotificationBackgroundService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
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
