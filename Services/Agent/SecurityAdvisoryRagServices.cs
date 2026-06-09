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

            ranked.Add(candidate switch
            {
                AdvisoryCandidate a => new SecurityAdvisorySearchResult(a.Advisory, null, a.ChunkText, score, vectorScore, a.TextScore),
                DocumentCandidate d => new SecurityAdvisorySearchResult(null, d.Document, d.ChunkText, score, vectorScore, d.TextScore),
                _ => throw new InvalidOperationException($"Unexpected candidate type: {candidate.GetType().Name}")
            });
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
    IAppSettingsService appSettingsService,
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
            if (result.Advisory is { } advisory)
            {
                builder.AppendLine($"- {BuildTitle(advisory)}");
                builder.AppendLine($"  狀態: {BuildRiskText(advisory)}");

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
        string systemPrompt,
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
        IReadOnlyList<AdvisoryConversationMessage>? history,
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

    private static readonly HashSet<string> CommonContextWords = new(StringComparer.OrdinalIgnoreCase)
    {
        "the", "and", "or", "for", "with", "about", "what", "how", "is", "are", "has", "have",
        "漏洞", "弱點", "版本", "風險", "資訊", "文件", "流程", "有", "嗎", "哪些", "最近"
    };
}
