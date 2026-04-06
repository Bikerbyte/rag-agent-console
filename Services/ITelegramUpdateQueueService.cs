using CPBLLineBotCloud.Models;

namespace CPBLLineBotCloud.Services;

/// <summary>
/// 管理 Telegram update 的收件佇列，讓 ingress 與實際處理流程可以拆開。
/// </summary>
public interface ITelegramUpdateQueueService
{
    Task<bool> EnqueueAsync(TelegramUpdate update, string sourceType, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<TelegramUpdateInbox>> ClaimBatchAsync(string workerName, int batchSize, TimeSpan leaseDuration, CancellationToken cancellationToken = default);
    Task MarkProcessedAsync(int inboxId, CancellationToken cancellationToken = default);
    Task MarkFailedAsync(int inboxId, string errorMessage, bool moveToDeadLetter, CancellationToken cancellationToken = default);
}
