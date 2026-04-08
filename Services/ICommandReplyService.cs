namespace CPBLLineBotCloud.Services;

/// <summary>
/// 將收到的 Telegram 指令文字整理成可直接回傳給使用者的訊息內容。
/// </summary>
public interface ICommandReplyService
{
    Task<string> BuildReplyAsync(string commandText, string? chatId = null, CancellationToken cancellationToken = default);
}
