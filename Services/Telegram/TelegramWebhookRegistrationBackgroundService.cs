using RagAgentConsole.Models;
using Microsoft.Extensions.Options;

namespace RagAgentConsole.Services;

/// <summary>
/// 在 webhook 模式下，啟動時主動把公開 webhook URL 註冊到 Telegram。
/// </summary>
public class TelegramWebhookRegistrationBackgroundService(
    IServiceScopeFactory scopeFactory,
    IOptions<AppRuntimeOptions> appRuntimeOptions,
    ILogger<TelegramWebhookRegistrationBackgroundService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await Task.Delay(TimeSpan.FromSeconds(2), stoppingToken);

        using var scope = scopeFactory.CreateScope();
        var appSettingsService = scope.ServiceProvider.GetRequiredService<IAppSettingsService>();
        var telegramBotClient = scope.ServiceProvider.GetRequiredService<ITelegramBotClient>();
        var botOptions = await appSettingsService.GetTelegramBotOptionsAsync(stoppingToken);
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
