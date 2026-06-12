using RagAgentConsole.Models;
using Microsoft.Extensions.Options;

namespace RagAgentConsole.Services;

/// <summary>
/// 定期執行 Telegram 排程推播判斷的背景工作迴圈。
/// </summary>
public class TelegramNotificationBackgroundService(
    IServiceScopeFactory scopeFactory,
    IOptions<PushNotificationOptions> pushOptions,
    ILogger<TelegramNotificationBackgroundService> logger) : BackgroundService
{
    // 單實例保證交給部署層（worker 單副本），這裡只做單純的定時迴圈。
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // 稍微等一下再跑，讓 migration 和 seed data 先完成。
        await DelayAsync(GetStartupDelay(), stoppingToken);

        while (!stoppingToken.IsCancellationRequested)
        {
            var intervalSeconds = Math.Max(30, pushOptions.Value.WorkerIntervalSeconds);

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
                logger.LogWarning(exception, "Telegram notification dispatch failed. Will retry on the next interval.");
            }

            await DelayAsync(TimeSpan.FromSeconds(intervalSeconds), stoppingToken);
        }
    }

    internal static async Task ExecuteDispatchOnceAsync(IServiceProvider serviceProvider, CancellationToken cancellationToken)
    {
        var dispatchService = serviceProvider.GetRequiredService<ITelegramNotificationDispatchService>();
        await dispatchService.ProcessPendingNotificationsAsync(cancellationToken);
    }

    protected virtual TimeSpan GetStartupDelay() => TimeSpan.FromSeconds(10);

    protected virtual Task ExecuteOwnedWorkAsync(IServiceProvider serviceProvider, CancellationToken cancellationToken)
        => ExecuteDispatchOnceAsync(serviceProvider, cancellationToken);

    protected virtual Task DelayAsync(TimeSpan delay, CancellationToken cancellationToken)
        => Task.Delay(delay, cancellationToken);
}
