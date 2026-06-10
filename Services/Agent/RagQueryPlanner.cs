using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using RagAgentConsole.Models;

namespace RagAgentConsole.Services;

public class RagQueryPlanner(
    IAiChatClient aiChatClient,
    IAppSettingsService appSettingsService,
    IRagDomainRegistry domainRegistry,
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

            // The planner JSON schema is intentionally kept flat (vendor,
            // cveId, riskFilter, …) so existing planner system prompts keep
            // working; the flat fields are mapped onto the generic
            // entity/filter dictionaries here and interpreted by the domain.
            var entities = new Dictionary<string, string?>();
            AddIfPresent(entities, PlanEntityKeys.Vendor, dto.Vendor);
            AddIfPresent(entities, PlanEntityKeys.Product, dto.Product);
            AddIfPresent(entities, PlanEntityKeys.Version, dto.Version);
            AddIfPresent(entities, SecurityAdvisoryPlanKeys.CveId, dto.CveId);

            var filters = new Dictionary<string, string?>();
            AddIfPresent(filters, SecurityAdvisoryPlanKeys.RiskFilter, dto.RiskFilter);
            AddIfPresent(filters, SecurityAdvisoryPlanKeys.CveYear, dto.CveYear?.ToString());

            var moduleName = domainRegistry.NormalizeModuleName(dto.ModuleName);
            var plan = new RetrievalPlan(
                question,
                dto.RetrievalQuery?.Trim() ?? string.Empty,
                NormalizeNullable(dto.Intent),
                keywords,
                dto.Notes?.Where(n => !string.IsNullOrWhiteSpace(n)).Select(n => n.Trim()).ToList() ?? [],
                entities,
                filters,
                moduleName,
                PlannerStrategy.Ai,
                ParseDate(dto.PublishedFrom),
                ParseDate(dto.PublishedTo),
                dto.PreferRecent ?? false);

            return domainRegistry.Resolve(moduleName).NormalizePlan(plan, question);
        }
        catch (JsonException ex)
        {
            logger.LogWarning(ex, "AI planner returned invalid JSON; using raw question as retrieval query.");
            return FallbackPlan(question);
        }
    }

    private RetrievalPlan FallbackPlan(string question)
    {
        var domain = domainRegistry.DefaultDomain;
        var plan = new RetrievalPlan(
            question,
            question.Trim(),
            "knowledge_lookup",
            [],
            [],
            RetrievalPlan.EmptyValues,
            RetrievalPlan.EmptyValues,
            domain.DefaultModuleName,
            PlannerStrategy.RawFallback);

        return domain.NormalizePlan(plan, question);
    }

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

    private static void AddIfPresent(Dictionary<string, string?> values, string key, string? value)
    {
        if (!string.IsNullOrWhiteSpace(value))
        {
            values[key] = value.Trim();
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
