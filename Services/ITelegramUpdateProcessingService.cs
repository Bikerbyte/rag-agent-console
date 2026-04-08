using CPBLLineBotCloud.Models;

namespace CPBLLineBotCloud.Services;

/// <summary>
/// 處理 Telegram update，讓 polling 與 webhook 可以共用同一套邏輯。
/// </summary>
public interface ITelegramUpdateProcessingService
{
    Task ProcessUpdateAsync(TelegramUpdate update, CancellationToken cancellationToken = default);
}
