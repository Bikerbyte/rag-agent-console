using System.Text;
using System.Text.RegularExpressions;
using RagAgentConsole.Models;
using Microsoft.Extensions.Options;

namespace RagAgentConsole.Services;

public class RagAnswerService(
    IRagRetrievalService searchService,
    IAppSettingsService appSettingsService,
    IRagDomainRegistry domainRegistry,
    IOptions<SecurityAdvisoryOptions> options,
    IOptions<AiProviderOptions> aiProviderOptions,
    IAiChatClient aiChatClient) : IRagAnswerService
{
    public async Task<string> BuildAnswerAsync(
        string question,
        IReadOnlyList<AgentConversationMessage>? history = null,
        CancellationToken cancellationToken = default)
        => (await BuildAnswerWithTraceAsync(question, history, cancellationToken)).Content;

    public async Task<AgentAnswerResult> BuildAnswerWithTraceAsync(
        string question,
        IReadOnlyList<AgentConversationMessage>? history = null,
        CancellationToken cancellationToken = default)
    {
        question = NormalizeMessage(question);
        if (string.IsNullOrWhiteSpace(question))
        {
            return new AgentAnswerResult(CapabilitiesReply, null);
        }

        var aiOptions = aiProviderOptions.Value;
        var agentOptions = await appSettingsService.GetAgentOptionsAsync(cancellationToken);

        var searchResponse = await searchService.SearchWithTraceAsync(question, history, options.Value.RagMaxChunks, cancellationToken: cancellationToken);

        var results = searchResponse.Results;
        var trace = BuildTrace(question, searchResponse);

        if (results.Count == 0)
        {
            var generalReply = await TryGenerateGeneralAnswerAsync(question, history, agentOptions.GeneralSystemPrompt, cancellationToken);
            return new AgentAnswerResult(
                string.IsNullOrWhiteSpace(generalReply) ? agentOptions.UnavailableReply : generalReply,
                trace);
        }

        var generated = await TryGenerateAnswerAsync(question, results, history, agentOptions.RagSystemPrompt, cancellationToken);
        if (!string.IsNullOrWhiteSpace(generated))
        {
            return new AgentAnswerResult(generated, trace);
        }

        // AI 未啟用（或仍是本機備援）時，不要把檢索片段硬湊成「答案」造成誤導，
        // 直接回覆固定訊息請使用者啟用 AI 對話模型。
        if (!aiOptions.EnableChatGeneration ||
            string.Equals(aiOptions.Provider, AiProviderNames.Local, StringComparison.OrdinalIgnoreCase))
        {
            return new AgentAnswerResult(agentOptions.UnavailableReply, trace);
        }

        // AI 已啟用但本次生成失敗（例如逾時或 API 錯誤）：退而提供清楚標示為「原始檢索」的整理。
        var builder = new StringBuilder();
        builder.AppendLine("AI 生成暫時無法使用，以下是根據目前已同步資料的原始檢索結果：");

        if (ContainsVersionLike(question))
        {
            builder.AppendLine();
            builder.AppendLine("你有提到特定版本或年份；目前命中的 context 不一定包含完整版本/日期範圍，因此以下只能根據已索引內容回答，不能直接判定未出現在 context 中的精確條件。");
        }

        foreach (var result in results)
        {
            builder.AppendLine();
            builder.AppendLine(domainRegistry.ResolveForResult(result).BuildPlainSummaryBlock(result));
        }

        return new AgentAnswerResult(builder.ToString().TrimEnd(), trace);
    }

    private async Task<string?> TryGenerateAnswerAsync(
        string question,
        IReadOnlyList<RetrievalResult> results,
        IReadOnlyList<AgentConversationMessage>? history,
        string systemPrompt,
        CancellationToken cancellationToken)
    {
        // Context formatting is domain-specific: advisory hits include CVE
        // metadata, uploaded documents only generic metadata.
        var contextBuilder = new StringBuilder();
        foreach (var result in results)
        {
            contextBuilder.AppendLine(domainRegistry.ResolveForResult(result).BuildContextBlock(result));
        }

        var userPrompt = $"""
        Conversation history:
        {BuildHistoryText(history)}

        Resolved follow-up context:
        {BuildFollowUpContext(question, history)}

        User question:
        {question}

        Context:
        {contextBuilder}
        """;

        return await aiChatClient.CompleteAsync(systemPrompt, userPrompt, cancellationToken);
    }

    private async Task<string?> TryGenerateGeneralAnswerAsync(
        string question,
        IReadOnlyList<AgentConversationMessage>? history,
        string systemPrompt,
        CancellationToken cancellationToken)
    {
        var userPrompt = $"""
        Conversation history:
        {BuildHistoryText(history)}

        User message:
        {question}
        """;

        return await aiChatClient.CompleteAsync(systemPrompt, userPrompt, cancellationToken);
    }

    private static string NormalizeMessage(string value)
        => string.Join(' ', value.Split(['\r', '\n', '\t'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)).Trim();

    private const string CapabilitiesReply =
        """
        你可以直接詢問知識庫中的資料，或請我整理特定主題的重點。

        例如：
        - 最近有什麼需要關注的資訊？
        - 幫我整理 [主題] 的處理建議
        - 這個問題應該怎麼處理？
        """;

    private static string Trim(string value, int maxLength)
    {
        var compact = string.Join(' ', value.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));
        return compact.Length <= maxLength ? compact : compact[..maxLength] + "...";
    }

    private AgentRetrievalTrace BuildTrace(string question, RetrievalResponse searchResponse)
        => new(
            question,
            searchResponse.Plan,
            searchResponse.RetrievalMode,
            searchResponse.Results.Select((result, index) => new AgentRetrievalMatch(
                index + 1,
                result.ModuleName,
                result.SourceKind,
                result.Title,
                result.SourceName,
                result.Score,
                result.VectorScore,
                result.TextScore,
                Trim(result.ChunkText, 260),
                domainRegistry.ResolveForResult(result).BuildTraceMetadata(result))).ToList());

    private static bool ContainsVersionLike(string value)
        => Regex.IsMatch(value, "(?<!cve-)\\b(?:\\d+(?:\\.\\d+){1,3}[a-z0-9-]*|\\d{4})\\b", RegexOptions.IgnoreCase);

    private static string BuildFollowUpContext(
        string question,
        IReadOnlyList<AgentConversationMessage>? history)
    {
        var currentVersion = ExtractVersion(question);
        var latestUserContext = history?
            .Where(message => string.Equals(message.Role, "user", StringComparison.OrdinalIgnoreCase))
            .Select(message => message.Content)
            .LastOrDefault(message => !string.IsNullOrWhiteSpace(message));

        var contextKeywords = ExtractContextKeywords(latestUserContext, currentVersion);
        if (string.IsNullOrWhiteSpace(currentVersion) && contextKeywords.Count == 0)
        {
            return "(none)";
        }

        var builder = new StringBuilder();
        if (!string.IsNullOrWhiteSpace(currentVersion))
        {
            builder.AppendLine($"Current version from the current user question: {currentVersion}.");
            builder.AppendLine("Treat this as the active version for this answer.");
            builder.AppendLine("Do not use older version numbers from conversation history as the active version.");
        }

        if (contextKeywords.Count > 0)
        {
            builder.AppendLine($"Prior context for omitted vendor/product names only: {string.Join(", ", contextKeywords)}.");
        }

        return builder.ToString().TrimEnd();
    }

    private static string? ExtractVersion(string value)
    {
        var match = Regex.Match(value, "(?<!cve-)\\b(?:\\d+(?:\\.\\d+){1,3}[a-z0-9-]*|\\d{4})\\b", RegexOptions.IgnoreCase);
        return match.Success ? match.Value : null;
    }

    private static IReadOnlyList<string> ExtractContextKeywords(string? value, string? currentVersion)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return [];
        }

        return Regex.Matches(value, "[a-z0-9_.:-]{2,}", RegexOptions.IgnoreCase)
            .Select(match => match.Value.Trim().Trim('.', ':', '-').ToLowerInvariant())
            .Where(keyword =>
                !string.Equals(keyword, currentVersion, StringComparison.OrdinalIgnoreCase) &&
                !ContainsVersionLike(keyword) &&
                !CommonContextWords.Contains(keyword))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(6)
            .ToList();
    }

    private static string BuildHistoryText(IReadOnlyList<AgentConversationMessage>? history)
    {
        if (history is null || history.Count == 0)
        {
            return "(none)";
        }

        var builder = new StringBuilder();
        foreach (var item in history.TakeLast(8))
        {
            builder.AppendLine($"{item.Role}: {Trim(item.Content, 600)}");
        }

        return builder.ToString().TrimEnd();
    }

    private static readonly HashSet<string> CommonContextWords = new(StringComparer.OrdinalIgnoreCase)
    {
        "the", "and", "or", "for", "with", "about", "what", "how", "is", "are", "has", "have",
        "漏洞", "弱點", "版本", "風險", "資訊", "文件", "流程", "有", "嗎", "哪些", "最近"
    };
}
