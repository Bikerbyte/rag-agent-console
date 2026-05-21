using SecurityAdvisoryBot.Models;

namespace SecurityAdvisoryBot.Services;

public interface IAdvisoryEmbeddingService
{
    Task<float[]> BuildEmbeddingAsync(string text, CancellationToken cancellationToken = default);
}

public interface IAiChatClient
{
    Task<string?> CompleteAsync(
        string systemPrompt,
        string userPrompt,
        CancellationToken cancellationToken = default);
}

public interface IAppSettingsService
{
    Task<AiProviderOptions> GetAiProviderOptionsAsync(CancellationToken cancellationToken = default);
    Task<TelegramBotOptions> GetTelegramBotOptionsAsync(CancellationToken cancellationToken = default);
    Task<DataSourceOptions> GetDataSourceOptionsAsync(CancellationToken cancellationToken = default);
    Task<PushNotificationOptions> GetPushNotificationOptionsAsync(CancellationToken cancellationToken = default);
    Task<IReadOnlyDictionary<string, AppSetting>> GetAllAsync(CancellationToken cancellationToken = default);
    Task SaveAsync(IEnumerable<AppSettingUpdate> updates, CancellationToken cancellationToken = default);
}

public sealed record AppSettingUpdate(string Key, string? Value, bool IsSecret = false);

public interface ISecurityAdvisoryAgentService
{
    Task<string> BuildReplyAsync(
        string messageText,
        string? chatId = null,
        IReadOnlyList<AdvisoryConversationMessage>? history = null,
        CancellationToken cancellationToken = default);
}

public interface IRuntimeLeadershipLeaseService
{
    Task<RuntimeLeadershipLeaseResult> TryAcquireOrRenewAsync(string leaseName, CancellationToken cancellationToken = default);
    Task<bool> IsCurrentLeaderAsync(string leaseName, CancellationToken cancellationToken = default);
    Task ReleaseIfOwnedAsync(string leaseName, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<RuntimeLeadershipLease>> GetActiveLeasesAsync(CancellationToken cancellationToken = default);
}

public interface ISecurityAdvisoryAnswerService
{
    Task<string> BuildAnswerAsync(
        string question,
        IReadOnlyList<AdvisoryConversationMessage>? history = null,
        CancellationToken cancellationToken = default);
}

public sealed record AdvisoryConversationMessage(string Role, string Content);

public interface ISecurityAdvisorySearchService
{
    Task<IReadOnlyList<SecurityAdvisorySearchResult>> SearchAsync(
        string question,
        int maxResults = 5,
        CancellationToken cancellationToken = default);
}

public interface ISecurityAdvisorySource
{
    string SourceName { get; }

    Task<IReadOnlyList<SecurityAdvisoryCandidate>> FetchLatestAsync(CancellationToken cancellationToken = default);
}

public interface ISecurityAdvisorySyncService
{
    Task<SecurityAdvisorySyncResult> SyncAsync(CancellationToken cancellationToken = default);
}

public interface ITelegramBotClient
{
    Task<TelegramBotProfile?> GetMeAsync(CancellationToken cancellationToken = default);
    Task<bool> DeleteWebhookAsync(CancellationToken cancellationToken = default);
    Task<bool> SetWebhookAsync(string webhookUrl, string? secretToken = null, bool dropPendingUpdates = false, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<TelegramUpdate>> GetUpdatesAsync(long? offset, CancellationToken cancellationToken = default);
    Task<TelegramSendResult> SendTextMessageAsync(string chatId, string messageText, CancellationToken cancellationToken = default);
    Task<bool> AnswerCallbackQueryAsync(string callbackQueryId, CancellationToken cancellationToken = default);
}

public interface ITelegramNotificationDispatchService
{
    Task ProcessPendingNotificationsAsync(CancellationToken cancellationToken = default);
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
