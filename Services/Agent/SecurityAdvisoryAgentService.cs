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
        你可以直接詢問弱點風險、CVE、CISA KEV、廠商或產品影響範圍。

        例如：
        - 最近 Cisco 有哪些高風險 CVE？
        - CVE-2024-3094 有什麼風險？
        - 今天有沒有 CISA KEV 新增項目？
        """;
}
