using CPBLLineBotCloud.Models;
using Microsoft.Extensions.Options;

namespace CPBLLineBotCloud.Services;

/// <summary>
/// 在 webhook 模式下，啟動時主動把公開 webhook URL 註冊到 Telegram。
/// </summary>
public class TelegramWebhookRegistrationBackgroundService(
    ITelegramBotClient telegramBotClient,
    IOptions<TelegramBotOptions> telegramBotOptions,
    IOptions<AppRuntimeOptions> appRuntimeOptions,
    ILogger<TelegramWebhookRegistrationBackgroundService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await Task.Delay(TimeSpan.FromSeconds(2), stoppingToken);

        var botOptions = telegramBotOptions.Value;
        var runtimeOptions = appRuntimeOptions.Value;

        if (!botOptions.Enabled || string.IsNullOrWhiteSpace(botOptions.BotToken))
        {
            logger.LogDebug("Skip Telegram webhook registration because bot is disabled or token is missing.");
            return;
        }

        if (!botOptions.UseWebhookMode)
        {
            logger.LogDebug("Skip Telegram webhook registration because webhook mode is disabled.");
            return;
        }

        if (!runtimeOptions.EnableTelegramWebhookIngress)
        {
            logger.LogDebug("Skip Telegram webhook registration because this node does not expose webhook ingress.");
            return;
        }

        if (string.IsNullOrWhiteSpace(botOptions.WebhookUrl))
        {
            logger.LogWarning("Telegram webhook mode is enabled, but WebhookUrl is empty. Skip webhook registration.");
            return;
        }

        try
        {
            var isSuccess = await telegramBotClient.SetWebhookAsync(
                botOptions.WebhookUrl,
                botOptions.WebhookSecretToken,
                dropPendingUpdates: false,
                stoppingToken);

            if (isSuccess)
            {
                logger.LogInformation("Telegram webhook registered. Url={WebhookUrl}", botOptions.WebhookUrl);
            }
            else
            {
                logger.LogWarning("Telegram webhook registration did not succeed. Url={WebhookUrl}", botOptions.WebhookUrl);
            }
        }
        catch (Exception exception)
        {
            logger.LogWarning(exception, "Telegram webhook registration failed.");
        }
    }
}
