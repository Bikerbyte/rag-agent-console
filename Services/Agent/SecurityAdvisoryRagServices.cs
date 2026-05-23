using System.Text;
using System.Text.RegularExpressions;
using SecurityAdvisoryBot.Models;
using Microsoft.Extensions.Options;

namespace SecurityAdvisoryBot.Services;

public partial class SecurityAdvisorySearchService(
    IAdvisoryEmbeddingService embeddingService,
    IAdvisoryVectorStore vectorStore,
    IAdvisoryQueryPlanner queryPlanner,
    IOptions<SecurityAdvisoryOptions> options,
    ILogger<SecurityAdvisorySearchService> logger) : ISecurityAdvisorySearchService
{
    public async Task<IReadOnlyList<SecurityAdvisorySearchResult>> SearchAsync(
        string question,
        int maxResults = 5,
        CancellationToken cancellationToken = default)
        => await SearchAsync(question, history: null, maxResults, cancellationToken);

    public async Task<IReadOnlyList<SecurityAdvisorySearchResult>> SearchAsync(
        string question,
        IReadOnlyList<AdvisoryConversationMessage>? history,
        int maxResults = 5,
        CancellationToken cancellationToken = default)
        => (await SearchWithTraceAsync(question, history, maxResults, cancellationToken: cancellationToken)).Results;

    public async Task<SecurityAdvisorySearchResponse> SearchWithTraceAsync(
        string question,
        IReadOnlyList<AdvisoryConversationMessage>? history = null,
        int maxResults = 5,
        string? moduleName = null,
        string retrievalMode = RetrievalModes.Hybrid,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(question))
        {
            return new SecurityAdvisorySearchResponse(
                new AdvisoryQueryPlan(question, string.Empty, null, null, null, null, null, "none", [], [], moduleName ?? KnowledgeModuleNames.CveAdvisory),
                RetrievalModes.Normalize(retrievalMode),
                []);
        }

        var plan = await queryPlanner.BuildPlanAsync(question, history, cancellationToken);
        var effectiveModule = string.IsNullOrWhiteSpace(moduleName) ? plan.ModuleName : moduleName.Trim();
        var effectiveMode = RetrievalModes.Normalize(retrievalMode);
        var queryVector = await embeddingService.BuildEmbeddingAsync(plan.RetrievalQuery, cancellationToken);
        var effectiveMax = Math.Clamp(maxResults, 1, Math.Max(1, options.Value.RagMaxChunks));
        var request = new AdvisoryVectorSearchRequest(
            plan.RetrievalQuery,
            plan.CveId,
            plan.Version,
            plan.KevOnly,
            plan.HighRiskOnly,
            plan.SearchKeywords,
            queryVector,
            effectiveMax,
            effectiveModule,
            effectiveMode);
        var candidates = await vectorStore.SearchAsync(request, cancellationToken);

        var ranked = new List<SecurityAdvisorySearchResult>();
        foreach (var candidate in candidates)
        {
            var vectorScore = CosineSimilarity(queryVector, candidate.Embedding);
            var score = effectiveMode switch
            {
                RetrievalModes.Vector => vectorScore,
                RetrievalModes.Keyword => candidate.TextScore,
                _ => vectorScore + candidate.TextScore
            };
            if (score <= 0)
            {
                continue;
            }

            ranked.Add(new SecurityAdvisorySearchResult(
                candidate.Advisory,
                candidate.Document,
                candidate.ChunkText,
                score,
                vectorScore,
                candidate.TextScore));
        }

        logger.LogDebug(
            "RAG search produced {CandidateCount} candidates and {RankedCount} ranked results. RetrievalQuery={RetrievalQuery}, Version={Version}.",
            candidates.Count,
            ranked.Count,
            plan.RetrievalQuery,
            plan.Version);

        var results = ranked
            .OrderByDescending(item => item.Score)
            .Take(effectiveMax)
            .ToList();

        return new SecurityAdvisorySearchResponse(
            plan with { ModuleName = effectiveModule },
            effectiveMode,
            results);
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
        => (await BuildAnswerWithTraceAsync(question, history, cancellationToken)).Content;

    public async Task<AgentAnswerResult> BuildAnswerWithTraceAsync(
        string question,
        IReadOnlyList<AdvisoryConversationMessage>? history = null,
        CancellationToken cancellationToken = default)
    {
        var aiOptions = aiProviderOptions.Value;
        if (!LooksLikeSecurityQuestion(question))
        {
            var generalReply = await TryGenerateGeneralAnswerAsync(question, history, cancellationToken);
            return new AgentAnswerResult(
                string.IsNullOrWhiteSpace(generalReply) ? BuildAiUnavailableReply() : generalReply,
                null);
        }

        var searchResponse = await searchService.SearchWithTraceAsync(question, history, options.Value.RagMaxChunks, cancellationToken: cancellationToken);
        var results = searchResponse.Results;
        var trace = BuildTrace(question, searchResponse);
        if (results.Count == 0)
        {
            return new AgentAnswerResult("目前資料庫裡找不到足夠相關的弱點資料。可以先同步資料，或換成較明確的產品、廠商、CVE ID 再問一次。", trace);
        }

        var generated = await TryGenerateAnswerAsync(question, results, history, cancellationToken);
        if (!string.IsNullOrWhiteSpace(generated))
        {
            return new AgentAnswerResult(generated, trace);
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

        if (ContainsVersionLike(question))
        {
            builder.AppendLine();
            builder.AppendLine("你有提到特定版本；目前資料庫主要包含 CVE、廠商、產品與風險狀態，沒有完整版本受影響範圍，因此以下只能確認相關產品已知弱點，不能直接判定該版本一定受影響。");
        }

        foreach (var result in results)
        {
            builder.AppendLine();
            if (result.Advisory is { } advisory)
            {
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
            else
            {
                builder.AppendLine($"- {result.Title}");
                builder.AppendLine($"  模組: {result.ModuleName}");
                builder.AppendLine($"  摘要: {Trim(result.ChunkText, 220)}");
                builder.AppendLine($"  來源: {result.SourceName}");
            }
        }

        return new AgentAnswerResult(builder.ToString().TrimEnd(), trace);
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
            contextBuilder.AppendLine($"Module: {result.ModuleName}");
            contextBuilder.AppendLine($"Source kind: {result.SourceKind}");
            contextBuilder.AppendLine($"CVE: {result.CveId}");
            contextBuilder.AppendLine($"Title: {result.Title}");
            contextBuilder.AppendLine($"Vendor: {result.Vendor}");
            contextBuilder.AppendLine($"Product: {result.Product}");
            contextBuilder.AppendLine($"Severity: {result.Severity}");
            contextBuilder.AppendLine($"CVSS: {result.CvssScore}");
            contextBuilder.AppendLine($"Known exploited: {result.IsKnownExploited}");
            contextBuilder.AppendLine($"Context chunk: {result.ChunkText}");
            contextBuilder.AppendLine($"Source: {result.SourceName} {result.SourceUrl}");
            contextBuilder.AppendLine();
        }

        var systemPrompt = """
        You are a security advisory assistant for a Telegram bot.
        Answer in Traditional Chinese unless the user asks otherwise.
        Use only the provided advisory context.
        Do not claim facts that are not present in the context.
        If the user asks about a specific product version but the advisory context does not include affected version ranges, say the current data is insufficient to confirm whether that exact version is affected.
        Use the conversation history only to resolve follow-up references such as omitted vendor or product names.
        The current user question always has priority over conversation history. If the current question contains a version, that version replaces any earlier version in the history.
        Do not merge an older version from conversation history with a newer follow-up version.
        For list questions, prioritize what should be handled first and explain why.
        Be concise, operational, and clear about uncertainty.
        Include CVE IDs, affected vendors/products, severity, exploitation status, recommended action, and sources when available.
        """;

        var userPrompt = $"""
        Conversation history:
        {BuildHistoryText(history)}

        Resolved follow-up context:
        {BuildFollowUpContext(question, history)}

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

    private static AgentRetrievalTrace BuildTrace(string question, SecurityAdvisorySearchResponse searchResponse)
        => new(
            question,
            searchResponse.Plan,
            searchResponse.RetrievalMode,
            searchResponse.Results.Select((result, index) => new AgentRetrievalMatch(
                index + 1,
                result.ModuleName,
                result.SourceKind,
                result.Title,
                result.CveId,
                result.Vendor,
                result.Product,
                result.Severity,
                result.IsKnownExploited,
                result.SourceName,
                result.Score,
                result.VectorScore,
                result.TextScore,
                Trim(result.ChunkText, 260))).ToList());

    private static bool LooksLikeSecurityQuestion(string question)
        => ContainsAny(question,
            "cve", "kev", "cvss", "critical", "漏洞", "弱點", "資安", "風險",
            "修補", "攻擊", "利用", "廠商", "產品", "高風險", "嚴重", "cisa", "nvd",
            "cisco", "fortinet", "microsoft", "windows", "azure", "linux", "openssl",
            "router", "firewall", "vpn", "exchange", "office", "adobe", "oracle", "citrix",
            "workflow", "runbook", "sop", "procedure", "policy", "compliance", "memo",
            "流程", "作業", "步驟", "政策", "合規", "內部文件");

    private static bool ContainsAny(string value, params string[] tokens)
        => tokens.Any(token => value.Contains(token, StringComparison.OrdinalIgnoreCase));

    private static bool ContainsVersionLike(string value)
        => Regex.IsMatch(value, "(?<!cve-)\\b(?:\\d+(?:\\.\\d+){1,3}[a-z0-9-]*|\\d{4})\\b", RegexOptions.IgnoreCase);

    private static string BuildFollowUpContext(
        string question,
        IReadOnlyList<AdvisoryConversationMessage>? history)
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

    private static readonly HashSet<string> CommonContextWords = new(StringComparer.OrdinalIgnoreCase)
    {
        "cve", "kev", "cvss", "risk", "high", "critical", "version",
        "漏洞", "弱點", "版本", "風險", "有弱點嗎"
    };
}
