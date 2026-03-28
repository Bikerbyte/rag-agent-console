namespace CPBLLineBotCloud.Services;

public interface ITelegramPushService
{
    Task<bool> SendPushAsync(string chatId, string messageTitle, string messageBody, string pushType, CancellationToken cancellationToken = default);
}
