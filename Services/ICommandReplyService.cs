namespace CPBLLineBotCloud.Services;

public interface ICommandReplyService
{
    Task<string> BuildReplyAsync(string commandText, string? chatId = null, CancellationToken cancellationToken = default);
}
