using System.Net.Http.Json;
using SecurityAdvisoryBot.Models;

namespace SecurityAdvisoryBot.Services;

/// <summary>
/// Wraps the Telegram Bot API calls used by this project.
/// </summary>
public class TelegramBotClient(
    HttpClient httpClient,
    IAppSettingsService appSettingsService,
    ILogger<TelegramBotClient> logger) : ITelegramBotClient
{
    public async Task<TelegramBotProfile?> GetMeAsync(CancellationToken cancellationToken = default)
    {
        var options = await appSettingsService.GetTelegramBotOptionsAsync(cancellationToken);
        if (!options.Enabled || string.IsNullOrWhiteSpace(options.BotToken))
        {
            return null;
        }

        var response = await httpClient.GetFromJsonAsync<TelegramBotProfileResponse>(BuildUri(options.ApiBaseUrl, $"/bot{options.BotToken}/getMe"), cancellationToken);
        return response?.Ok == true ? response.Result : null;
    }

    public async Task<bool> DeleteWebhookAsync(CancellationToken cancellationToken = default)
    {
        var options = await appSettingsService.GetTelegramBotOptionsAsync(cancellationToken);
        if (!options.Enabled || string.IsNullOrWhiteSpace(options.BotToken))
        {
            return false;
        }

        var response = await httpClient.PostAsync(BuildUri(options.ApiBaseUrl, $"/bot{options.BotToken}/deleteWebhook?drop_pending_updates=false"), content: null, cancellationToken);
        if (response.IsSuccessStatusCode)
        {
            return true;
        }

        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        logger.LogWarning("Telegram deleteWebhook failed. StatusCode={StatusCode}, Body={Body}", response.StatusCode, body);
        return false;
    }

    public async Task<bool> SetWebhookAsync(string webhookUrl, string? secretToken = null, bool dropPendingUpdates = false, CancellationToken cancellationToken = default)
    {
        var options = await appSettingsService.GetTelegramBotOptionsAsync(cancellationToken);
        if (!options.Enabled || string.IsNullOrWhiteSpace(options.BotToken))
        {
            return false;
        }

        if (string.IsNullOrWhiteSpace(webhookUrl))
        {
            logger.LogWarning("Telegram setWebhook skipped because webhookUrl is empty.");
            return false;
        }

        var request = new TelegramSetWebhookRequest
        {
            Url = webhookUrl,
            SecretToken = string.IsNullOrWhiteSpace(secretToken) ? null : secretToken,
            DropPendingUpdates = dropPendingUpdates
        };

        var response = await httpClient.PostAsJsonAsync(BuildUri(options.ApiBaseUrl, $"/bot{options.BotToken}/setWebhook"), request, cancellationToken);
        if (response.IsSuccessStatusCode)
        {
            return true;
        }

        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        logger.LogWarning("Telegram setWebhook failed. StatusCode={StatusCode}, Body={Body}", response.StatusCode, body);
        return false;
    }

    public async Task<IReadOnlyList<TelegramUpdate>> GetUpdatesAsync(long? offset, CancellationToken cancellationToken = default)
    {
        var options = await appSettingsService.GetTelegramBotOptionsAsync(cancellationToken);
        if (!options.Enabled || string.IsNullOrWhiteSpace(options.BotToken))
        {
            return [];
        }

        var path = $"/bot{options.BotToken}/getUpdates?timeout=20";
        if (offset.HasValue)
        {
            path += $"&offset={offset.Value}";
        }

        var response = await httpClient.GetFromJsonAsync<TelegramUpdateResponse>(BuildUri(options.ApiBaseUrl, path), cancellationToken);
        return response?.Ok == true ? response.Result : [];
    }

    public async Task<TelegramSendResult> SendTextMessageAsync(string chatId, string messageText, CancellationToken cancellationToken = default)
    {
        var options = await appSettingsService.GetTelegramBotOptionsAsync(cancellationToken);
        if (!options.Enabled || string.IsNullOrWhiteSpace(options.BotToken))
        {
            logger.LogInformation("Telegram bot is disabled or bot token is missing. Skipping sendMessage call.");
            return new TelegramSendResult
            {
                IsSuccess = false,
                ErrorMessage = "Telegram bot is disabled or bot token is missing."
            };
        }

        var request = new TelegramSendMessageRequest
        {
            ChatId = chatId,
            Text = messageText,
            ReplyMarkup = BuildDefaultInlineKeyboard()
        };

        var response = await httpClient.PostAsJsonAsync(BuildUri(options.ApiBaseUrl, $"/bot{options.BotToken}/sendMessage"), request, cancellationToken);
        if (response.IsSuccessStatusCode)
        {
            return new TelegramSendResult
            {
                IsSuccess = true
            };
        }

        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        var errorMessage = $"Telegram sendMessage failed. StatusCode={(int)response.StatusCode}.";
        logger.LogWarning("Telegram sendMessage failed. StatusCode={StatusCode}, Body={Body}", response.StatusCode, body);

        return new TelegramSendResult
        {
            IsSuccess = false,
            ErrorMessage = errorMessage
        };
    }

    public async Task<bool> AnswerCallbackQueryAsync(string callbackQueryId, CancellationToken cancellationToken = default)
    {
        var options = await appSettingsService.GetTelegramBotOptionsAsync(cancellationToken);
        if (!options.Enabled || string.IsNullOrWhiteSpace(options.BotToken))
        {
            return false;
        }

        var request = new TelegramAnswerCallbackQueryRequest
        {
            CallbackQueryId = callbackQueryId
        };

        var response = await httpClient.PostAsJsonAsync(BuildUri(options.ApiBaseUrl, $"/bot{options.BotToken}/answerCallbackQuery"), request, cancellationToken);
        if (response.IsSuccessStatusCode)
        {
            return true;
        }

        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        logger.LogWarning("Telegram answerCallbackQuery failed. StatusCode={StatusCode}, Body={Body}", response.StatusCode, body);
        return false;
    }

    private static TelegramInlineKeyboardMarkup BuildDefaultInlineKeyboard()
        => new()
        {
            InlineKeyboard =
            [
                [
                    new TelegramInlineKeyboardButton { Text = "Latest CVEs", CallbackData = "列出最近新增或更新的 CVE" },
                    new TelegramInlineKeyboardButton { Text = "Critical", CallbackData = "列出最近高風險 Critical CVE" }
                ],
                [
                    new TelegramInlineKeyboardButton { Text = "CISA KEV", CallbackData = "列出最近 CISA KEV 已知遭利用弱點" },
                    new TelegramInlineKeyboardButton { Text = "Cisco risk", CallbackData = "最近 Cisco 有哪些高風險 CVE？" }
                ],
                [
                    new TelegramInlineKeyboardButton { Text = "How to ask", CallbackData = "我可以問你哪些弱點管理問題？" }
                ]
            ]
        };

    public static Uri BuildUri(string baseUrl, string path)
        => new($"{baseUrl.TrimEnd('/')}/{path.TrimStart('/')}");
}
