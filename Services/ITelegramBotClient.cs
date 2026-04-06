namespace CPBLLineBotCloud.Services;

/// <summary>
/// 給 polling、回覆與排程推播共用的 Telegram Bot API 輕量封裝。
/// </summary>
public interface ITelegramBotClient
{
    Task<CPBLLineBotCloud.Models.TelegramBotProfile?> GetMeAsync(CancellationToken cancellationToken = default);
    Task<bool> DeleteWebhookAsync(CancellationToken cancellationToken = default);
    Task<bool> SetWebhookAsync(string webhookUrl, string? secretToken = null, bool dropPendingUpdates = false, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<CPBLLineBotCloud.Models.TelegramUpdate>> GetUpdatesAsync(long? offset, CancellationToken cancellationToken = default);
    Task<CPBLLineBotCloud.Models.TelegramSendResult> SendTextMessageAsync(string chatId, string messageText, CancellationToken cancellationToken = default);
    Task<bool> AnswerCallbackQueryAsync(string callbackQueryId, CancellationToken cancellationToken = default);
}
