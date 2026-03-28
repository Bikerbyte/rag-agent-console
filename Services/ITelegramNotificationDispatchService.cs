namespace CPBLLineBotCloud.Services;

public interface ITelegramNotificationDispatchService
{
    Task ProcessPendingNotificationsAsync(CancellationToken cancellationToken = default);
}
