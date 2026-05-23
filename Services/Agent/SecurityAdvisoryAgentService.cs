using SecurityAdvisoryBot.Models;

namespace SecurityAdvisoryBot.Services;

public class SecurityAdvisoryAgentService(
    ISecurityAdvisoryAnswerService answerService,
    ILogger<SecurityAdvisoryAgentService> logger) : ISecurityAdvisoryAgentService
{
    public async Task<string> BuildReplyAsync(
        string messageText,
        string? chatId = null,
        IReadOnlyList<AdvisoryConversationMessage>? history = null,
        CancellationToken cancellationToken = default)
        => (await BuildReplyWithTraceAsync(messageText, chatId, history, cancellationToken)).Content;

    public async Task<AgentAnswerResult> BuildReplyWithTraceAsync(
        string messageText,
        string? chatId = null,
        IReadOnlyList<AdvisoryConversationMessage>? history = null,
        CancellationToken cancellationToken = default)
    {
        var normalized = NormalizeMessage(messageText);
        if (string.IsNullOrWhiteSpace(normalized))
        {
            return new AgentAnswerResult(BuildCapabilitiesReply(), null);
        }

        logger.LogInformation(
            "Building security advisory agent reply. ChatId={ChatId}, HasHistory={HasHistory}",
            chatId,
            history is { Count: > 0 });

        return await answerService.BuildAnswerWithTraceAsync(normalized, history, cancellationToken);
    }

    private static string NormalizeMessage(string value)
        => string.Join(' ', value.Split(['\r', '\n', '\t'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)).Trim();

    private static string BuildCapabilitiesReply()
        => """
        你可以直接詢問知識庫中的資料，或請我整理特定主題的重點。

        例如：
        - 最近有什麼需要關注的資訊？
        - 幫我整理 [主題] 的處理建議
        - 這個問題應該怎麼處理？
        """;
}
