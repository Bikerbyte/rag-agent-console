using System.Text.Json;
using CPBLLineBotCloud.Data;
using CPBLLineBotCloud.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace CPBLLineBotCloud.Services;

/// <summary>
/// 用資料庫表暫時承接 Telegram update。
/// 先讓多台 VM 有共享收件佇列，之後若要換 Service Bus 或 RabbitMQ，主要替換這層即可。
/// </summary>
public class TelegramUpdateQueueService(
    ApplicationDbContext dbContext,
    IOptions<AppRuntimeOptions> runtimeOptions,
    ILogger<TelegramUpdateQueueService> logger) : ITelegramUpdateQueueService
{
    private static readonly JsonSerializerOptions JsonSerializerOptions = new(JsonSerializerDefaults.Web);

    public async Task<bool> EnqueueAsync(TelegramUpdate update, string sourceType, CancellationToken cancellationToken = default)
    {
        var exists = await dbContext.TelegramUpdateInboxes
            .AnyAsync(item => item.UpdateId == update.UpdateId, cancellationToken);

        if (exists)
        {
            return false;
        }

        var now = DateTimeOffset.UtcNow;
        var queueItem = new TelegramUpdateInbox
        {
            UpdateId = update.UpdateId,
            SourceType = sourceType,
            Status = "Pending",
            PayloadJson = JsonSerializer.Serialize(update, JsonSerializerOptions),
            IngressNode = runtimeOptions.Value.InstanceName,
            AttemptCount = 0,
            EnqueuedTime = now,
            LastUpdatedTime = now
        };

        dbContext.TelegramUpdateInboxes.Add(queueItem);

        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
            return true;
        }
        catch (DbUpdateException exception)
        {
            logger.LogDebug(exception, "Telegram update {UpdateId} was already queued by another node.", update.UpdateId);
            dbContext.Entry(queueItem).State = EntityState.Detached;
            return false;
        }
    }

    public async Task<IReadOnlyList<TelegramUpdateInbox>> ClaimBatchAsync(
        string workerName,
        int batchSize,
        TimeSpan leaseDuration,
        CancellationToken cancellationToken = default)
    {
        var now = DateTimeOffset.UtcNow;
        var leaseUntil = now.Add(leaseDuration);

        var candidateIds = await dbContext.TelegramUpdateInboxes
            .Where(item =>
                item.Status == "Pending" ||
                (item.Status == "Processing" && item.LeaseUntil.HasValue && item.LeaseUntil < now))
            .OrderBy(item => item.EnqueuedTime)
            .Select(item => item.TelegramUpdateInboxId)
            .Take(batchSize)
            .ToListAsync(cancellationToken);

        if (candidateIds.Count == 0)
        {
            return [];
        }

        var claimedIds = new List<int>();

        foreach (var candidateId in candidateIds)
        {
            var affectedRows = await dbContext.TelegramUpdateInboxes
                .Where(item =>
                    item.TelegramUpdateInboxId == candidateId &&
                    (item.Status == "Pending" ||
                     (item.Status == "Processing" && item.LeaseUntil.HasValue && item.LeaseUntil < now)))
                .ExecuteUpdateAsync(setters => setters
                    .SetProperty(item => item.Status, "Processing")
                    .SetProperty(item => item.ProcessingNode, workerName)
                    .SetProperty(item => item.ProcessingStartedTime, now)
                    .SetProperty(item => item.LeaseUntil, leaseUntil)
                    .SetProperty(item => item.AttemptCount, item => item.AttemptCount + 1)
                    .SetProperty(item => item.LastUpdatedTime, now),
                    cancellationToken);

            if (affectedRows > 0)
            {
                claimedIds.Add(candidateId);
            }
        }

        if (claimedIds.Count == 0)
        {
            return [];
        }

        return await dbContext.TelegramUpdateInboxes
            .Where(item => claimedIds.Contains(item.TelegramUpdateInboxId))
            .OrderBy(item => item.EnqueuedTime)
            .ToListAsync(cancellationToken);
    }

    public async Task MarkProcessedAsync(int inboxId, CancellationToken cancellationToken = default)
    {
        var now = DateTimeOffset.UtcNow;

        await dbContext.TelegramUpdateInboxes
            .Where(item => item.TelegramUpdateInboxId == inboxId)
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(item => item.Status, "Processed")
                .SetProperty(item => item.ProcessedTime, now)
                .SetProperty(item => item.LeaseUntil, (DateTimeOffset?)null)
                .SetProperty(item => item.LastError, (string?)null)
                .SetProperty(item => item.LastUpdatedTime, now),
                cancellationToken);
    }

    public async Task MarkFailedAsync(int inboxId, string errorMessage, bool moveToDeadLetter, CancellationToken cancellationToken = default)
    {
        var now = DateTimeOffset.UtcNow;
        var nextStatus = moveToDeadLetter ? "Failed" : "Pending";
        var trimmedErrorMessage = errorMessage.Length <= 500 ? errorMessage : errorMessage.Substring(0, 500);

        await dbContext.TelegramUpdateInboxes
            .Where(item => item.TelegramUpdateInboxId == inboxId)
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(item => item.Status, nextStatus)
                .SetProperty(item => item.LeaseUntil, (DateTimeOffset?)null)
                .SetProperty(item => item.LastError, trimmedErrorMessage)
                .SetProperty(item => item.LastUpdatedTime, now),
                cancellationToken);
    }
}
