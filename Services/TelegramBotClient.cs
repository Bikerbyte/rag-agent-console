using System.Net.Http.Json;
using CPBLLineBotCloud.Models;
using Microsoft.Extensions.Options;

namespace CPBLLineBotCloud.Services;

public class TelegramBotClient(HttpClient httpClient, IOptions<TelegramBotOptions> options, ILogger<TelegramBotClient> logger) : ITelegramBotClient
{
    public async Task<TelegramBotProfile?> GetMeAsync(CancellationToken cancellationToken = default)
    {
        var telegramBotOptions = options.Value;

        if (!telegramBotOptions.Enabled || string.IsNullOrWhiteSpace(telegramBotOptions.BotToken))
        {
            return null;
        }

        var response = await httpClient.GetFromJsonAsync<TelegramBotProfileResponse>($"/bot{telegramBotOptions.BotToken}/getMe", cancellationToken);
        return response?.Ok == true ? response.Result : null;
    }

    public async Task<bool> DeleteWebhookAsync(CancellationToken cancellationToken = default)
    {
        var telegramBotOptions = options.Value;

        if (!telegramBotOptions.Enabled || string.IsNullOrWhiteSpace(telegramBotOptions.BotToken))
        {
            return false;
        }

        var response = await httpClient.PostAsync($"/bot{telegramBotOptions.BotToken}/deleteWebhook?drop_pending_updates=false", content: null, cancellationToken);

        if (response.IsSuccessStatusCode)
        {
            return true;
        }

        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        logger.LogWarning("Telegram deleteWebhook failed. StatusCode={StatusCode}, Body={Body}", response.StatusCode, body);
        return false;
    }

    public async Task<IReadOnlyList<TelegramUpdate>> GetUpdatesAsync(long? offset, CancellationToken cancellationToken = default)
    {
        var telegramBotOptions = options.Value;

        if (!telegramBotOptions.Enabled || string.IsNullOrWhiteSpace(telegramBotOptions.BotToken))
        {
            return [];
        }

        var path = $"/bot{telegramBotOptions.BotToken}/getUpdates?timeout=20";

        if (offset.HasValue)
        {
            path += $"&offset={offset.Value}";
        }

        var response = await httpClient.GetFromJsonAsync<TelegramUpdateResponse>(path, cancellationToken);
        return response?.Ok == true ? response.Result : [];
    }

    public async Task<TelegramSendResult> SendTextMessageAsync(string chatId, string messageText, CancellationToken cancellationToken = default)
    {
        var telegramBotOptions = options.Value;

        if (!telegramBotOptions.Enabled || string.IsNullOrWhiteSpace(telegramBotOptions.BotToken))
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

        var response = await httpClient.PostAsJsonAsync($"/bot{telegramBotOptions.BotToken}/sendMessage", request, cancellationToken);

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
        var telegramBotOptions = options.Value;

        if (!telegramBotOptions.Enabled || string.IsNullOrWhiteSpace(telegramBotOptions.BotToken))
        {
            return false;
        }

        var request = new TelegramAnswerCallbackQueryRequest
        {
            CallbackQueryId = callbackQueryId
        };

        var response = await httpClient.PostAsJsonAsync($"/bot{telegramBotOptions.BotToken}/answerCallbackQuery", request, cancellationToken);
        if (response.IsSuccessStatusCode)
        {
            return true;
        }

        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        logger.LogWarning("Telegram answerCallbackQuery failed. StatusCode={StatusCode}, Body={Body}", response.StatusCode, body);
        return false;
    }

    private static TelegramInlineKeyboardMarkup BuildDefaultInlineKeyboard()
    {
        return new TelegramInlineKeyboardMarkup
        {
            InlineKeyboard =
            [
                [
                    new TelegramInlineKeyboardButton { Text = "今天賽程", CallbackData = "/today" },
                    new TelegramInlineKeyboardButton { Text = "明日賽程", CallbackData = "/tomorrow" }
                ],
                [
                    new TelegramInlineKeyboardButton { Text = "即時比分", CallbackData = "/live" },
                    new TelegramInlineKeyboardButton { Text = "下一場", CallbackData = "/next" }
                ],
                [
                    new TelegramInlineKeyboardButton { Text = "我的追蹤", CallbackData = "/following" },
                    new TelegramInlineKeyboardButton { Text = "最新新聞", CallbackData = "/news" }
                ],
                [
                    new TelegramInlineKeyboardButton { Text = "今日 recap", CallbackData = "/recap" },
                    new TelegramInlineKeyboardButton { Text = "幫助", CallbackData = "/help" }
                ],
            ]
        };
    }
}
