namespace CPBLLineBotCloud.Services;

/// <summary>
/// 在每次背景工作循環中，判斷有哪些 Telegram 排程推播需要送出。
/// </summary>
public interface ITelegramNotificationDispatchService
{
    Task ProcessPendingNotificationsAsync(CancellationToken cancellationToken = default);
}
