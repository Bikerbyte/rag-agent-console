using System.Net.Http.Json;
using RagAgentConsole.Models;

namespace RagAgentConsole.Services;

public interface ITelegramBotClient
{
    Task<TelegramBotProfile?> GetMeAsync(CancellationToken cancellationToken = default);
    Task<bool> DeleteWebhookAsync(CancellationToken cancellationToken = default);
    Task<bool> SetWebhookAsync(string webhookUrl, string? secretToken = null, bool dropPendingUpdates = false, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<TelegramUpdate>> GetUpdatesAsync(long? offset, CancellationToken cancellationToken = default);
    Task<TelegramSendResult> SendTextMessageAsync(string chatId, string messageText, CancellationToken cancellationToken = default);
    Task<bool> AnswerCallbackQueryAsync(string callbackQueryId, CancellationToken cancellationToken = default);
}

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
                    new TelegramInlineKeyboardButton { Text = "Policies", CallbackData = "知識庫有哪些政策文件？" },
                    new TelegramInlineKeyboardButton { Text = "Runbooks", CallbackData = "列出知識庫中的作業流程與 runbook" }
                ],
                [
                    new TelegramInlineKeyboardButton { Text = "Password policy", CallbackData = "一般員工密碼多久會過期？" },
                    new TelegramInlineKeyboardButton { Text = "Backup policy", CallbackData = "備份資料需要使用什麼加密？" }
                ],
                [
                    new TelegramInlineKeyboardButton { Text = "How to ask", CallbackData = "我可以問你哪些知識庫問題？" }
                ]
            ]
        };

    public static Uri BuildUri(string baseUrl, string path)
        => new($"{baseUrl.TrimEnd('/')}/{path.TrimStart('/')}");
}
