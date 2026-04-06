using System.Text.Json;
using CPBLLineBotCloud.Models;
using Microsoft.Extensions.Options;

namespace CPBLLineBotCloud.Services;

/// <summary>
/// 從 Telegram update 佇列取出待處理資料，交給實際的 update processor。
/// </summary>
public class TelegramUpdateQueueBackgroundService(
    IServiceScopeFactory scopeFactory,
    IOptions<AppRuntimeOptions> appRuntimeOptions,
    ILogger<TelegramUpdateQueueBackgroundService> logger) : BackgroundService
{
    private static readonly TimeSpan LeaseDuration = TimeSpan.FromMinutes(2);
    private static readonly JsonSerializerOptions JsonSerializerOptions = new(JsonSerializerDefaults.Web);
    private const int BatchSize = 10;
    private const int MaxAttempts = 5;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await Task.Delay(TimeSpan.FromSeconds(2), stoppingToken);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                using var scope = scopeFactory.CreateScope();
                var queueService = scope.ServiceProvider.GetRequiredService<ITelegramUpdateQueueService>();
                var updateProcessingService = scope.ServiceProvider.GetRequiredService<ITelegramUpdateProcessingService>();
                var workerName = appRuntimeOptions.Value.InstanceName;

                var queuedItems = await queueService.ClaimBatchAsync(workerName, BatchSize, LeaseDuration, stoppingToken);

                if (queuedItems.Count == 0)
                {
                    await Task.Delay(TimeSpan.FromSeconds(2), stoppingToken);
                    continue;
                }

                foreach (var queuedItem in queuedItems)
                {
                    try
                    {
                        var update = JsonSerializer.Deserialize<TelegramUpdate>(queuedItem.PayloadJson, JsonSerializerOptions);
                        if (update is null)
                        {
                            throw new InvalidOperationException($"Telegram update payload {queuedItem.TelegramUpdateInboxId} could not be deserialized.");
                        }

                        await updateProcessingService.ProcessUpdateAsync(update, stoppingToken);
                        await queueService.MarkProcessedAsync(queuedItem.TelegramUpdateInboxId, stoppingToken);
                    }
                    catch (Exception exception)
                    {
                        var moveToDeadLetter = queuedItem.AttemptCount >= MaxAttempts;
                        await queueService.MarkFailedAsync(queuedItem.TelegramUpdateInboxId, exception.Message, moveToDeadLetter, stoppingToken);

                        logger.LogWarning(
                            exception,
                            "Telegram update queue item {InboxId} failed. Attempt={AttemptCount}, MoveToDeadLetter={MoveToDeadLetter}",
                            queuedItem.TelegramUpdateInboxId,
                            queuedItem.AttemptCount,
                            moveToDeadLetter);
                    }
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception exception)
            {
                logger.LogWarning(exception, "Telegram update queue worker failed. Will retry on next loop.");
                await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken);
            }
        }
    }
}
