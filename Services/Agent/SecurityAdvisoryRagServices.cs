using System.Text;
using System.Text.RegularExpressions;
using SecurityAdvisoryBot.Models;
using Microsoft.Extensions.Options;

namespace SecurityAdvisoryBot.Services;

public partial class SecurityAdvisorySearchService(
    IAdvisoryEmbeddingService embeddingService,
    IAdvisoryVectorStore vectorStore,
    IOptions<SecurityAdvisoryOptions> options,
    ILogger<SecurityAdvisorySearchService> logger) : ISecurityAdvisorySearchService
{
    public async Task<IReadOnlyList<SecurityAdvisorySearchResult>> SearchAsync(
        string question,
        int maxResults = 5,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(question))
        {
            return [];
        }

        var profile = RetrievalProfile.FromQuestion(question);
        var queryVector = await embeddingService.BuildEmbeddingAsync(question, cancellationToken);
        var effectiveMax = Math.Clamp(maxResults, 1, Math.Max(1, options.Value.RagMaxChunks));
        var request = new AdvisoryVectorSearchRequest(
            question,
            profile.CveId,
            profile.KevOnly,
            profile.HighRiskOnly,
            profile.Keywords,
            queryVector,
            effectiveMax);
        var candidates = await vectorStore.SearchAsync(request, cancellationToken);

        var ranked = new List<SecurityAdvisorySearchResult>();
        foreach (var candidate in candidates)
        {
            var score = CosineSimilarity(queryVector, candidate.Embedding) + candidate.TextScore;
            if (score <= 0)
            {
                continue;
            }

            ranked.Add(new SecurityAdvisorySearchResult(candidate.Advisory, candidate.ChunkText, score));
        }

        logger.LogDebug("RAG search produced {CandidateCount} candidates and {RankedCount} ranked results.", candidates.Count, ranked.Count);

        return ranked
            .OrderByDescending(item => item.Score)
            .Take(effectiveMax)
            .ToList();
    }

    private static double CosineSimilarity(IReadOnlyList<float> left, IReadOnlyList<float> right)
    {
        var length = Math.Min(left.Count, right.Count);
        if (length == 0)
        {
            return 0;
        }

        var sum = 0d;
        for (var index = 0; index < length; index++)
        {
            sum += left[index] * right[index];
        }

        return sum;
    }

    private sealed partial class RetrievalProfile
    {
        private RetrievalProfile(string question)
        {
            CveId = ExtractCveId(question);
            KevOnly = ContainsAny(question, "kev", "known exploited", "已知遭利用", "被利用");
            HighRiskOnly = ContainsAny(question, "critical", "high risk", "高風險", "嚴重", "cvss");
            Keywords = ExtractKeywords(question);
        }

        public string? CveId { get; }
        public bool KevOnly { get; }
        public bool HighRiskOnly { get; }
        public IReadOnlyList<string> Keywords { get; }

        public static RetrievalProfile FromQuestion(string question)
            => new(question);

        private static string? ExtractCveId(string value)
        {
            var match = CveRegex().Match(value);
            return match.Success ? match.Value.ToUpperInvariant() : null;
        }

        private static IReadOnlyList<string> ExtractKeywords(string question)
        {
            var keywords = new List<string>();
            foreach (Match match in KeywordRegex().Matches(question.ToLowerInvariant()))
            {
                if (!StopWords.Contains(match.Value))
                {
                    keywords.Add(match.Value);
                }
            }

            return keywords.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        }

        private static bool ContainsAny(string value, params string[] tokens)
            => tokens.Any(token => value.Contains(token, StringComparison.OrdinalIgnoreCase));

        private static readonly HashSet<string> StopWords = new(StringComparer.OrdinalIgnoreCase)
        {
            "ask", "help", "latest", "critical", "kev", "cve", "sync", "watch", "follow",
            "list", "show", "risk", "high", "recent", "today", "this", "week"
        };

        [GeneratedRegex("cve-\\d{4}-\\d{4,}", RegexOptions.IgnoreCase)]
        private static partial Regex CveRegex();

        [GeneratedRegex("[a-z0-9_.:-]{2,}")]
        private static partial Regex KeywordRegex();
    }
}

public class SecurityAdvisoryAnswerService(
    ISecurityAdvisorySearchService searchService,
    IOptions<SecurityAdvisoryOptions> options,
    IOptions<AiProviderOptions> aiProviderOptions,
    IAiChatClient aiChatClient) : ISecurityAdvisoryAnswerService
{
    public async Task<string> BuildAnswerAsync(
        string question,
        IReadOnlyList<AdvisoryConversationMessage>? history = null,
        CancellationToken cancellationToken = default)
    {
        var aiOptions = aiProviderOptions.Value;
        if (!LooksLikeSecurityQuestion(question))
        {
            var generalReply = await TryGenerateGeneralAnswerAsync(question, history, cancellationToken);
            return string.IsNullOrWhiteSpace(generalReply) ? BuildAiUnavailableReply() : generalReply;
        }

        var results = await searchService.SearchAsync(question, options.Value.RagMaxChunks, cancellationToken);
        if (results.Count == 0)
        {
            return "目前資料庫裡找不到足夠相關的弱點資料。可以先同步資料，或換成較明確的產品、廠商、CVE ID 再問一次。";
        }

        var generated = await TryGenerateAnswerAsync(question, results, history, cancellationToken);
        if (!string.IsNullOrWhiteSpace(generated))
        {
            return generated;
        }

        var builder = new StringBuilder();
        if (!aiOptions.EnableChatGeneration ||
            string.Equals(aiOptions.Provider, AiProviderNames.Local, StringComparison.OrdinalIgnoreCase))
        {
            builder.AppendLine("目前尚未啟用 AI 對話模型，以下是根據已同步資料產生的檢索結果：");
        }
        else
        {
            builder.AppendLine("根據目前已同步的弱點資料，整理如下：");
        }

        foreach (var result in results)
        {
            var advisory = result.Advisory;
            builder.AppendLine();
            builder.AppendLine($"- {BuildTitle(advisory)}");
            builder.AppendLine($"  風險: {BuildRiskText(advisory)}");

            var summary = advisory.AiSummary ?? advisory.Description;
            builder.AppendLine($"  摘要: {Trim(summary, 220)}");

            if (!string.IsNullOrWhiteSpace(advisory.SuggestedAction))
            {
                builder.AppendLine($"  建議: {Trim(advisory.SuggestedAction, 180)}");
            }

            builder.AppendLine($"  來源: {advisory.SourceName} {advisory.SourceUrl}");
        }

        return builder.ToString().TrimEnd();
    }

    private async Task<string?> TryGenerateAnswerAsync(
        string question,
        IReadOnlyList<SecurityAdvisorySearchResult> results,
        IReadOnlyList<AdvisoryConversationMessage>? history,
        CancellationToken cancellationToken)
    {
        var contextBuilder = new StringBuilder();
        foreach (var result in results)
        {
            var advisory = result.Advisory;
            contextBuilder.AppendLine($"CVE: {advisory.CveId ?? advisory.ExternalId}");
            contextBuilder.AppendLine($"Title: {advisory.Title}");
            contextBuilder.AppendLine($"Vendor: {advisory.Vendor}");
            contextBuilder.AppendLine($"Product: {advisory.Product}");
            contextBuilder.AppendLine($"Severity: {advisory.Severity}");
            contextBuilder.AppendLine($"CVSS: {advisory.CvssScore}");
            contextBuilder.AppendLine($"Known exploited: {advisory.IsKnownExploited}");
            contextBuilder.AppendLine($"Summary: {advisory.AiSummary ?? advisory.Description}");
            contextBuilder.AppendLine($"Suggested action: {advisory.SuggestedAction ?? advisory.RequiredAction}");
            contextBuilder.AppendLine($"Source: {advisory.SourceName} {advisory.SourceUrl}");
            contextBuilder.AppendLine();
        }

        var systemPrompt = """
        You are a security advisory assistant for a Telegram bot.
        Answer in Traditional Chinese unless the user asks otherwise.
        Use only the provided advisory context.
        Do not claim facts that are not present in the context.
        Use the conversation history only to resolve follow-up references.
        For list questions, prioritize what should be handled first and explain why.
        Be concise, operational, and clear about uncertainty.
        Include CVE IDs, affected vendors/products, severity, exploitation status, recommended action, and sources when available.
        """;

        var userPrompt = $"""
        Conversation history:
        {BuildHistoryText(history)}

        User question:
        {question}

        Advisory context:
        {contextBuilder}
        """;

        return await aiChatClient.CompleteAsync(systemPrompt, userPrompt, cancellationToken);
    }

    private async Task<string?> TryGenerateGeneralAnswerAsync(
        string question,
        IReadOnlyList<AdvisoryConversationMessage>? history,
        CancellationToken cancellationToken)
    {
        var systemPrompt = """
        You are a security advisory assistant inside an operations console.
        Answer in Traditional Chinese.
        If the user is greeting or testing the chat, briefly explain that you can help analyze CVEs, KEV items, vendors, products, and watchlists.
        Do not invent vulnerability facts without retrieved context.
        """;

        var userPrompt = $"""
        Conversation history:
        {BuildHistoryText(history)}

        User message:
        {question}
        """;

        return await aiChatClient.CompleteAsync(systemPrompt, userPrompt, cancellationToken);
    }

    private static string BuildTitle(SecurityAdvisory advisory)
        => string.IsNullOrWhiteSpace(advisory.CveId)
            ? advisory.Title
            : $"{advisory.CveId} - {advisory.Title}";

    private static string BuildRiskText(SecurityAdvisory advisory)
    {
        var parts = new List<string>();
        if (!string.IsNullOrWhiteSpace(advisory.Severity))
        {
            parts.Add(advisory.Severity);
        }

        if (advisory.CvssScore.HasValue)
        {
            parts.Add($"CVSS {advisory.CvssScore:0.0}");
        }

        if (advisory.IsKnownExploited)
        {
            parts.Add("known exploited");
        }

        if (advisory.HasRansomwareUse)
        {
            parts.Add("ransomware use");
        }

        return parts.Count == 0 ? "未標示" : string.Join(" / ", parts);
    }

    private static string Trim(string value, int maxLength)
    {
        var compact = string.Join(' ', value.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));
        return compact.Length <= maxLength ? compact : compact[..maxLength] + "...";
    }

    private static bool LooksLikeSecurityQuestion(string question)
        => ContainsAny(question,
            "cve", "kev", "cvss", "critical", "漏洞", "弱點", "資安", "風險",
            "修補", "攻擊", "利用", "廠商", "產品", "高風險", "嚴重", "cisa", "nvd",
            "cisco", "fortinet", "microsoft", "windows", "azure", "linux", "openssl",
            "router", "firewall", "vpn", "exchange", "office", "adobe", "oracle", "citrix");

    private static bool ContainsAny(string value, params string[] tokens)
        => tokens.Any(token => value.Contains(token, StringComparison.OrdinalIgnoreCase));

    private static string BuildHistoryText(IReadOnlyList<AdvisoryConversationMessage>? history)
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

    private static string BuildAiUnavailableReply()
        => """
        目前尚未啟用 AI 對話模型，所以我不能像一般聊天機器人一樣接續閒聊。

        你現在仍可以測試 RAG / 弱點資料流程，例如：
        - CVE-2024-3094 有什麼風險？
        - 最近 Cisco 有哪些高風險 CVE？
        - 今天有沒有 CISA KEV 新增項目？

        若要啟用完整對話能力，請設定 OpenAI 或 Ollama，並將 AiProvider:EnableChatGeneration 設為 true。
        """;
}
