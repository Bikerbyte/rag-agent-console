namespace CPBLLineBotCloud.Services;

/// <summary>
/// 負責送出 Telegram 推播，並記錄每次送出的結果。
/// </summary>
public interface ITelegramPushService
{
    Task<bool> SendPushAsync(string chatId, string messageTitle, string messageBody, string pushType, CancellationToken cancellationToken = default);
}
