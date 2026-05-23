using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using SecurityAdvisoryBot.Models;
using Microsoft.Extensions.Options;

namespace SecurityAdvisoryBot.Services;

/// <summary>
/// Builds query plans using only regex/heuristic logic, with no AI dependency.
/// </summary>
public partial class LocalAdvisoryQueryPlanner(
    ILogger<LocalAdvisoryQueryPlanner> logger) : IAdvisoryQueryPlanner
{
    public Task<AdvisoryQueryPlan> BuildPlanAsync(
        string question,
        IReadOnlyList<AdvisoryConversationMessage>? history = null,
        CancellationToken cancellationToken = default)
    {
        logger.LogDebug("Building local heuristic query plan for question length {Length}.", question.Length);
        return Task.FromResult(BuildLocalPlan(question, history));
    }

    internal static AdvisoryQueryPlan BuildLocalPlan(
        string question,
        IReadOnlyList<AdvisoryConversationMessage>? history)
    {
        var cveId = NormalizeCve(CveRegex().Match(question).Value);
        var version = ExtractVersion(question);
        var riskFilter = ExtractRiskFilter(question);
        var moduleName = ExtractModuleName(question);
        var keywords = ExtractKeywords(question, version).ToList();
        var historyKeywords = ExtractHistoryKeywords(history, version);
        foreach (var keyword in historyKeywords)
        {
            if (!keywords.Contains(keyword, StringComparer.OrdinalIgnoreCase))
            {
                keywords.Add(keyword);
            }
        }

        keywords = keywords.Take(8).ToList();
        var retrievalQuery = BuildRetrievalQuery(cveId, keywords, riskFilter);
        if (string.IsNullOrWhiteSpace(retrievalQuery))
        {
            retrievalQuery = question.Trim();
        }

        var notes = new List<string>();

        if (!string.IsNullOrWhiteSpace(version))
        {
            notes.Add("version is supporting context, not a hard retrieval filter");
        }
        if (historyKeywords.Count > 0)
        {
            notes.Add("follow-up context was used to complete the retrieval query");
        }

        return new AdvisoryQueryPlan(
            question,
            retrievalQuery,
            "knowledge_lookup",
            keywords.ElementAtOrDefault(0),
            keywords.ElementAtOrDefault(1),
            version,
            cveId,
            riskFilter,
            keywords,
            notes,
            moduleName,
            PlannerStrategy.LocalHeuristic);
    }

    internal static string BuildRetrievalQuery(string? cveId, IReadOnlyList<string> keywords, string? riskFilter)
    {
        if (!string.IsNullOrWhiteSpace(cveId))
        {
            return cveId;
        }

        var builder = new StringBuilder();
        foreach (var keyword in keywords.Take(5))
        {
            builder.Append(keyword).Append(' ');
        }

        if (string.Equals(riskFilter, "known_exploited", StringComparison.OrdinalIgnoreCase))
        {
            builder.Append("known exploited ");
        }
        else if (string.Equals(riskFilter, "critical", StringComparison.OrdinalIgnoreCase) ||
                 string.Equals(riskFilter, "high_risk", StringComparison.OrdinalIgnoreCase))
        {
            builder.Append("critical high risk ");
        }

        return builder.ToString().Trim();
    }

    internal static IReadOnlyList<string> NormalizeKeywords(IEnumerable<string>? keywords, string? version)
        => (keywords ?? [])
            .Select(keyword => keyword.Trim().Trim('.', ':', '-').ToLowerInvariant())
            .Where(keyword => !string.IsNullOrWhiteSpace(keyword))
            .Where(keyword => !StopWords.Contains(keyword))
            .Where(keyword => !IsVersionKeyword(keyword, version))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(8)
            .ToList();

    internal static string? NormalizeCve(string? value)
        => string.IsNullOrWhiteSpace(value) ? null : value.Trim().ToUpperInvariant();

    internal static string ExtractModuleName(string question)
    {
        if (ContainsAny(question, "workflow", "runbook", "process", "procedure", "sop", "流程", "作業", "步驟"))
        {
            return KnowledgeModuleNames.WorkflowQa;
        }

        if (ContainsAny(question, "memo", "policy", "compliance", "內部", "政策", "合規"))
        {
            return KnowledgeModuleNames.InternalDocs;
        }

        return KnowledgeModuleNames.CveAdvisory;
    }

    internal static string BuildHistoryText(IReadOnlyList<AdvisoryConversationMessage>? history)
    {
        if (history is null || history.Count == 0)
        {
            return "(none)";
        }

        return string.Join(Environment.NewLine, history.TakeLast(6).Select(item => $"{item.Role}: {item.Content}"));
    }

    internal static bool ContainsAny(string value, params string[] tokens)
        => tokens.Any(token => value.Contains(token, StringComparison.OrdinalIgnoreCase));

    private static IReadOnlyList<string> ExtractKeywords(string question, string? version)
    {
        var keywords = new List<string>();
        foreach (Match match in KeywordRegex().Matches(question.ToLowerInvariant()))
        {
            var keyword = match.Value.Trim('.', ':', '-');
            if (string.IsNullOrWhiteSpace(keyword) ||
                StopWords.Contains(keyword) ||
                IsVersionKeyword(keyword, version) ||
                CveRegex().IsMatch(keyword))
            {
                continue;
            }

            keywords.Add(keyword);
        }

        return NormalizeKeywords(keywords, version);
    }

    private static IReadOnlyList<string> ExtractHistoryKeywords(
        IReadOnlyList<AdvisoryConversationMessage>? history,
        string? version)
    {
        if (history is null || history.Count == 0)
        {
            return [];
        }

        var latestUserContext = history
            .Where(message => string.Equals(message.Role, "user", StringComparison.OrdinalIgnoreCase))
            .Select(message => message.Content)
            .LastOrDefault(message => !string.IsNullOrWhiteSpace(message));

        if (string.IsNullOrWhiteSpace(latestUserContext))
        {
            return [];
        }

        return ExtractKeywords(latestUserContext, version)
            .Take(4)
            .ToList();
    }

    private static string? ExtractVersion(string question)
    {
        var match = VersionRegex().Match(question);
        return match.Success ? match.Value : null;
    }

    private static string? ExtractRiskFilter(string question)
    {
        if (ContainsAny(question, "kev", "known exploited", "已知遭利用", "被利用"))
        {
            return "known_exploited";
        }

        if (ContainsAny(question, "critical", "嚴重"))
        {
            return "critical";
        }

        if (ContainsAny(question, "high risk", "高風險", "cvss"))
        {
            return "high_risk";
        }

        return "none";
    }

    private static bool IsVersionKeyword(string keyword, string? version)
        => (!string.IsNullOrWhiteSpace(version) && string.Equals(keyword, version, StringComparison.OrdinalIgnoreCase)) ||
           VersionRegex().IsMatch(keyword);

    private static readonly HashSet<string> StopWords = new(StringComparer.OrdinalIgnoreCase)
    {
        "ask", "help", "latest", "recent", "today", "this", "week", "list", "show",
        "risk", "high", "critical", "sync", "watch", "follow", "version",
        "has", "have", "with", "about", "known", "exploited",
        "版本", "有", "嗎", "哪些", "最近"
    };

    [GeneratedRegex("cve-\\d{4}-\\d{4,}", RegexOptions.IgnoreCase)]
    internal static partial Regex CveRegex();

    [GeneratedRegex("(?<!cve-)\\b(?:\\d+(?:\\.\\d+){1,3}[a-z0-9-]*|\\d{4})\\b", RegexOptions.IgnoreCase)]
    private static partial Regex VersionRegex();

    [GeneratedRegex("[a-z0-9_.:-]{2,}")]
    private static partial Regex KeywordRegex();
}

/// <summary>
/// Tries the AI planner first; falls back to local heuristics on failure or when AI is disabled.
/// Sets PlannerStrategy on the returned plan so callers know which path was used.
/// </summary>
public partial class ResilientAdvisoryQueryPlanner(
    IAiChatClient aiChatClient,
    LocalAdvisoryQueryPlanner localPlanner,
    IOptions<AiProviderOptions> aiProviderOptions,
    IAppSettingsService appSettingsService,
    ILogger<ResilientAdvisoryQueryPlanner> logger) : IAdvisoryQueryPlanner
{
    public async Task<AdvisoryQueryPlan> BuildPlanAsync(
        string question,
        IReadOnlyList<AdvisoryConversationMessage>? history = null,
        CancellationToken cancellationToken = default)
    {
        var options = aiProviderOptions.Value;
        if (options.EnableChatGeneration &&
            !string.Equals(options.Provider, AiProviderNames.Local, StringComparison.OrdinalIgnoreCase))
        {
            var aiPlan = await TryBuildWithAiAsync(question, history, cancellationToken);
            if (aiPlan is not null)
            {
                return aiPlan;
            }
        }

        return await localPlanner.BuildPlanAsync(question, history, cancellationToken);
    }

    private async Task<AdvisoryQueryPlan?> TryBuildWithAiAsync(
        string question,
        IReadOnlyList<AdvisoryConversationMessage>? history,
        CancellationToken cancellationToken)
    {
        var agentOptions = await appSettingsService.GetAgentOptionsAsync(cancellationToken);
        var systemPrompt = agentOptions.PlannerSystemPrompt;

        var userPrompt = $$"""
        Conversation history:
        {{LocalAdvisoryQueryPlanner.BuildHistoryText(history)}}

        User question:
        {{question}}

        Expected JSON shape:
        {
          "intent": "knowledge_lookup",
          "moduleName": "CveAdvisory",
          "vendor": "Citrix",
          "product": "NetScaler",
          "version": "59.22",
          "cveId": null,
          "riskFilter": "none",
          "retrievalQuery": "Citrix NetScaler vulnerabilities",
          "searchKeywords": ["citrix", "netscaler"],
          "notes": ["version is supporting context, not a hard retrieval filter"]
        }
        """;

        var response = await aiChatClient.CompleteAsync(systemPrompt, userPrompt, cancellationToken);
        if (string.IsNullOrWhiteSpace(response))
        {
            return null;
        }

        try
        {
            var json = StripJsonFence(response);
            var dto = JsonSerializer.Deserialize<QueryPlanDto>(json, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            });

            if (dto is null)
            {
                return null;
            }

            var keywords = LocalAdvisoryQueryPlanner.NormalizeKeywords(dto.SearchKeywords, dto.Version);
            var retrievalQuery = string.IsNullOrWhiteSpace(dto.RetrievalQuery)
                ? LocalAdvisoryQueryPlanner.BuildRetrievalQuery(dto.CveId, keywords, dto.RiskFilter)
                : dto.RetrievalQuery.Trim();

            return new AdvisoryQueryPlan(
                question,
                retrievalQuery,
                NormalizeNullable(dto.Intent),
                NormalizeNullable(dto.Vendor),
                NormalizeNullable(dto.Product),
                NormalizeNullable(dto.Version),
                LocalAdvisoryQueryPlanner.NormalizeCve(dto.CveId),
                NormalizeRiskFilter(dto.RiskFilter),
                keywords,
                dto.Notes?.Where(note => !string.IsNullOrWhiteSpace(note)).Select(note => note.Trim()).ToList() ?? [],
                NormalizeModuleName(dto.ModuleName, dto.Intent, question),
                PlannerStrategy.Ai);
        }
        catch (JsonException exception)
        {
            logger.LogDebug(exception, "AI query planner returned invalid JSON.");
            return null;
        }
    }

    private static string StripJsonFence(string value)
    {
        var trimmed = value.Trim();
        if (trimmed.StartsWith("```", StringComparison.Ordinal))
        {
            trimmed = Regex.Replace(trimmed, "^```(?:json)?\\s*", string.Empty, RegexOptions.IgnoreCase);
            trimmed = Regex.Replace(trimmed, "\\s*```$", string.Empty);
        }

        return trimmed.Trim();
    }

    private static string? NormalizeNullable(string? value)
        => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static string NormalizeRiskFilter(string? value)
        => string.IsNullOrWhiteSpace(value) ? "none" : value.Trim().ToLowerInvariant();

    private static string NormalizeModuleName(string? value, string? intent, string question)
    {
        var normalized = value?.Trim();
        if (string.Equals(normalized, KnowledgeModuleNames.CveAdvisory, StringComparison.OrdinalIgnoreCase))
        {
            return KnowledgeModuleNames.CveAdvisory;
        }
        if (string.Equals(normalized, KnowledgeModuleNames.WorkflowQa, StringComparison.OrdinalIgnoreCase))
        {
            return KnowledgeModuleNames.WorkflowQa;
        }
        if (string.Equals(normalized, KnowledgeModuleNames.InternalDocs, StringComparison.OrdinalIgnoreCase))
        {
            return KnowledgeModuleNames.InternalDocs;
        }

        if (LocalAdvisoryQueryPlanner.ContainsAny(intent ?? string.Empty, "workflow", "runbook", "sop"))
        {
            return KnowledgeModuleNames.WorkflowQa;
        }

        return LocalAdvisoryQueryPlanner.ExtractModuleName(question);
    }

    private sealed record QueryPlanDto(
        string? Intent,
        string? Vendor,
        string? Product,
        string? Version,
        string? CveId,
        string? RiskFilter,
        string? ModuleName,
        string? RetrievalQuery,
        IReadOnlyList<string>? SearchKeywords,
        IReadOnlyList<string>? Notes);
}
