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

public interface ITelegramPushService
{
    Task<bool> SendPushAsync(string chatId, string messageTitle, string messageBody, string pushType, CancellationToken cancellationToken = default);
}

public interface ITelegramUpdateProcessingService
{
    Task ProcessUpdateAsync(TelegramUpdate update, CancellationToken cancellationToken = default);
}

public interface ITelegramUpdateQueueService
{
    Task<bool> EnqueueAsync(TelegramUpdate update, string sourceType, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<TelegramUpdateInbox>> ClaimBatchAsync(string workerName, int batchSize, TimeSpan leaseDuration, CancellationToken cancellationToken = default);
    Task MarkProcessedAsync(int inboxId, CancellationToken cancellationToken = default);
    Task MarkFailedAsync(int inboxId, string errorMessage, bool moveToDeadLetter, CancellationToken cancellationToken = default);
}
