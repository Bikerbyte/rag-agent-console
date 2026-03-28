namespace CPBLLineBotCloud.Services;

public interface ITelegramBotClient
{
    Task<CPBLLineBotCloud.Models.TelegramBotProfile?> GetMeAsync(CancellationToken cancellationToken = default);
    Task<bool> DeleteWebhookAsync(CancellationToken cancellationToken = default);
    Task<IReadOnlyList<CPBLLineBotCloud.Models.TelegramUpdate>> GetUpdatesAsync(long? offset, CancellationToken cancellationToken = default);
    Task<CPBLLineBotCloud.Models.TelegramSendResult> SendTextMessageAsync(string chatId, string messageText, CancellationToken cancellationToken = default);
    Task<bool> AnswerCallbackQueryAsync(string callbackQueryId, CancellationToken cancellationToken = default);
}
