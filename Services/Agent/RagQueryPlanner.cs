using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using RagAgentConsole.Models;

namespace RagAgentConsole.Services;

public class RagQueryPlanner(
    IAiChatClient aiChatClient,
    IAppSettingsService appSettingsService,
    ILogger<RagQueryPlanner> logger) : IRagQueryPlanner
{
    public async Task<RetrievalPlan> BuildPlanAsync(
        string question,
        IReadOnlyList<AgentConversationMessage>? history = null,
        CancellationToken cancellationToken = default)
    {
        // Planner failure must not break read-only RAG retrieval: when the AI
        // planner is disabled or fails, fall back to the raw question instead
        // of throwing, so retrieval and evaluation keep working without AI.
        try
        {
            var options = await appSettingsService.GetAiProviderOptionsAsync(cancellationToken);
            if (options.EnableChatGeneration &&
                !string.Equals(options.Provider, AiProviderNames.Local, StringComparison.OrdinalIgnoreCase))
            {
                return await BuildWithAiAsync(question, history, cancellationToken);
            }

            logger.LogDebug("AI planner is disabled; using raw question as retrieval query.");
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception)
        {
            logger.LogWarning(exception, "AI planner failed; falling back to raw question.");
        }

        return FallbackPlan(question);
    }

    private async Task<RetrievalPlan> BuildWithAiAsync(
        string question,
        IReadOnlyList<AgentConversationMessage>? history,
        CancellationToken cancellationToken)
    {
        var agentOptions = await appSettingsService.GetAgentOptionsAsync(cancellationToken);
        var systemPrompt = agentOptions.PlannerSystemPrompt;

        var userPrompt = $$"""
        Conversation history:
        {{BuildHistoryText(history)}}

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
          "notes": ["version is supporting context, not a hard retrieval filter"],
          "publishedFrom": null,
          "publishedTo": null,
          "preferRecent": false,
          "cveYear": null
        }
        """;

        var response = await aiChatClient.CompleteAsync(systemPrompt, userPrompt, cancellationToken);
        if (string.IsNullOrWhiteSpace(response))
        {
            logger.LogWarning("AI planner returned empty response; using raw question as retrieval query.");
            return FallbackPlan(question);
        }

        try
        {
            var json = StripJsonFence(response);
            var dto = JsonSerializer.Deserialize<QueryPlanDto>(json, JsonOptions);
            if (dto is null)
            {
                logger.LogWarning("AI planner returned null after deserialization; using raw question.");
                return FallbackPlan(question);
            }

            var keywords = NormalizeKeywords(dto.SearchKeywords);
            var retrievalQuery = string.IsNullOrWhiteSpace(dto.RetrievalQuery)
                ? BuildRetrievalQuery(dto.CveId, keywords, dto.RiskFilter)
                : dto.RetrievalQuery.Trim();

            return new RetrievalPlan(
                question,
                retrievalQuery,
                NormalizeNullable(dto.Intent),
                NormalizeNullable(dto.Vendor),
                NormalizeNullable(dto.Product),
                NormalizeNullable(dto.Version),
                NormalizeCve(dto.CveId),
                NormalizeRiskFilter(dto.RiskFilter),
                keywords,
                dto.Notes?.Where(n => !string.IsNullOrWhiteSpace(n)).Select(n => n.Trim()).ToList() ?? [],
                NormalizeModuleName(dto.ModuleName),
                PlannerStrategy.Ai,
                ParseDate(dto.PublishedFrom),
                ParseDate(dto.PublishedTo),
                dto.PreferRecent ?? false,
                dto.CveYear);
        }
        catch (JsonException ex)
        {
            logger.LogWarning(ex, "AI planner returned invalid JSON; using raw question as retrieval query.");
            return FallbackPlan(question);
        }
    }

    private static RetrievalPlan FallbackPlan(string question)
        => new(question, question.Trim(), "knowledge_lookup", null, null, null, null, "none", [], [],
            KnowledgeModuleNames.CveAdvisory, PlannerStrategy.RawFallback);

    internal static string BuildHistoryText(IReadOnlyList<AgentConversationMessage>? history)
    {
        if (history is null || history.Count == 0)
            return "(none)";
        return string.Join(Environment.NewLine, history.TakeLast(6).Select(item => $"{item.Role}: {item.Content}"));
    }

    internal static IReadOnlyList<string> NormalizeKeywords(IEnumerable<string>? keywords)
        => (keywords ?? [])
            .Select(k => k.Trim().ToLowerInvariant())
            .Where(k => !string.IsNullOrWhiteSpace(k))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(8)
            .ToList();

    internal static string? NormalizeCve(string? value)
        => string.IsNullOrWhiteSpace(value) ? null : value.Trim().ToUpperInvariant();

    internal static string BuildRetrievalQuery(string? cveId, IReadOnlyList<string> keywords, string? riskFilter)
    {
        if (!string.IsNullOrWhiteSpace(cveId))
            return cveId;

        var parts = keywords.Take(5).ToList();
        if (string.Equals(riskFilter, "known_exploited", StringComparison.OrdinalIgnoreCase))
            parts.Add("known exploited");
        else if (string.Equals(riskFilter, "critical", StringComparison.OrdinalIgnoreCase) ||
                 string.Equals(riskFilter, "high_risk", StringComparison.OrdinalIgnoreCase))
            parts.Add("critical high risk");

        return string.Join(' ', parts).Trim();
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

    private static string NormalizeModuleName(string? value)
    {
        var normalized = value?.Trim();
        if (string.Equals(normalized, KnowledgeModuleNames.WorkflowQa, StringComparison.OrdinalIgnoreCase))
            return KnowledgeModuleNames.WorkflowQa;
        if (string.Equals(normalized, KnowledgeModuleNames.InternalDocs, StringComparison.OrdinalIgnoreCase))
            return KnowledgeModuleNames.InternalDocs;
        return KnowledgeModuleNames.CveAdvisory;
    }

    private static DateTimeOffset? ParseDate(string? value)
        => string.IsNullOrWhiteSpace(value) ? null
            : DateTimeOffset.TryParse(value, out var d) ? d : null;

    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };

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
        IReadOnlyList<string>? Notes,
        string? PublishedFrom,
        string? PublishedTo,
        bool? PreferRecent,
        int? CveYear);
}
