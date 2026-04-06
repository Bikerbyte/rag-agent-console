using CPBLLineBotCloud.Models;
using Microsoft.Extensions.Options;

namespace CPBLLineBotCloud.Services;

/// <summary>
/// 用 long polling 持續接收 Telegram update 的背景工作。
/// 適合本機開發或 webhook 還沒完整接好時先使用。
/// </summary>
public class TelegramPollingBackgroundService(
    IServiceScopeFactory scopeFactory,
    ITelegramBotClient telegramBotClient,
    IOptions<TelegramBotOptions> options,
    ILogger<TelegramPollingBackgroundService> logger) : BackgroundService
{
    private long? _offset;
    private bool _webhookResetCompleted;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // 這裡故意保持簡單和耐用，Telegram 或本地回覆流程出錯就留到下一輪再試。
        while (!stoppingToken.IsCancellationRequested)
        {
            var telegramBotOptions = options.Value;

            if (!telegramBotOptions.Enabled || string.IsNullOrWhiteSpace(telegramBotOptions.BotToken))
            {
                await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken);
                continue;
            }

            if (telegramBotOptions.UseWebhookMode)
            {
                await Task.Delay(TimeSpan.FromSeconds(10), stoppingToken);
                continue;
            }

            try
            {
                if (!_webhookResetCompleted)
                {
                    // long polling 和 webhook 不應該同時搶同一批 update。
                    await telegramBotClient.DeleteWebhookAsync(stoppingToken);
                    _webhookResetCompleted = true;
                    logger.LogInformation("Telegram webhook reset for long polling mode.");
                }

                var updates = await telegramBotClient.GetUpdatesAsync(_offset, stoppingToken);

                foreach (var update in updates)
                {
                    _offset = update.UpdateId + 1;
                    using var scope = scopeFactory.CreateScope();
                    var updateQueueService = scope.ServiceProvider.GetRequiredService<ITelegramUpdateQueueService>();
                    await updateQueueService.EnqueueAsync(update, "Polling", stoppingToken);
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception exception)
            {
                logger.LogWarning(exception, "Telegram polling loop failed. Retrying after delay.");
                await Task.Delay(TimeSpan.FromSeconds(Math.Max(3, telegramBotOptions.PollingDelaySeconds)), stoppingToken);
            }
        }
    }

}
