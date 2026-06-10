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

        Expected JSON shape (entities/filters keys depend on the domain;
        the example shows a security advisory question):
        {
          "intent": "knowledge_lookup",
          "domain": "security_advisory",
          "moduleName": "CveAdvisory",
          "retrievalQuery": "Citrix NetScaler vulnerabilities",
          "searchKeywords": ["citrix", "netscaler"],
          "entities": {
            "vendor": "Citrix",
            "product": "NetScaler",
            "version": "59.22",
            "cveId": null
          },
          "filters": {
            "riskFilter": "none",
            "cveYear": null
          },
          "notes": ["version is supporting context, not a hard retrieval filter"],
          "publishedFrom": null,
          "publishedTo": null,
          "preferRecent": false
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

            // The planner accepts both the generic schema (entities/filters
            // dictionaries) and the legacy flat fields (vendor, cveId,
            // riskFilter, …) so previously stored planner system prompts keep
            // working. Flat fields are mapped first; dictionary values from
            // the new schema take precedence on conflict.
            var entities = new Dictionary<string, string?>();
            AddIfPresent(entities, PlanEntityKeys.Vendor, dto.Vendor);
            AddIfPresent(entities, PlanEntityKeys.Product, dto.Product);
            AddIfPresent(entities, PlanEntityKeys.Version, dto.Version);
            AddIfPresent(entities, SecurityAdvisoryPlanKeys.CveId, dto.CveId);
            MergeValues(entities, dto.Entities);

            var filters = new Dictionary<string, string?>();
            AddIfPresent(filters, SecurityAdvisoryPlanKeys.RiskFilter, dto.RiskFilter);
            AddIfPresent(filters, SecurityAdvisoryPlanKeys.CveYear, dto.CveYear?.ToString());
            MergeValues(filters, dto.Filters);

            var moduleName = ResolveModuleName(dto.ModuleName, dto.Domain);
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

    private string ResolveModuleName(string? moduleName, string? domainName)
    {
        if (!string.IsNullOrWhiteSpace(moduleName))
        {
            return domainRegistry.NormalizeModuleName(moduleName);
        }

        // No module given: let the planner-selected domain pick its default
        // module before falling back to the registry default.
        var domain = domainRegistry.FindByName(domainName);
        return domain?.DefaultModuleName ?? domainRegistry.DefaultDomain.DefaultModuleName;
    }

    private static void AddIfPresent(Dictionary<string, string?> values, string key, string? value)
    {
        if (!string.IsNullOrWhiteSpace(value))
        {
            values[key] = value.Trim();
        }
    }

    private static void MergeValues(
        Dictionary<string, string?> values,
        IReadOnlyDictionary<string, JsonElement>? overrides)
    {
        if (overrides is null)
        {
            return;
        }

        foreach (var (key, element) in overrides)
        {
            if (string.IsNullOrWhiteSpace(key))
            {
                continue;
            }

            // The LLM may emit strings, numbers, or booleans; nulls clear
            // any value the legacy flat fields put there.
            var value = element.ValueKind switch
            {
                JsonValueKind.String => element.GetString(),
                JsonValueKind.Number => element.GetRawText(),
                JsonValueKind.True => "true",
                JsonValueKind.False => "false",
                _ => null
            };

            if (string.IsNullOrWhiteSpace(value))
            {
                values.Remove(key.Trim());
            }
            else
            {
                values[key.Trim()] = value.Trim();
            }
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
        string? Domain,
        string? ModuleName,
        string? RetrievalQuery,
        IReadOnlyList<string>? SearchKeywords,
        IReadOnlyDictionary<string, JsonElement>? Entities,
        IReadOnlyDictionary<string, JsonElement>? Filters,
        IReadOnlyList<string>? Notes,
        string? PublishedFrom,
        string? PublishedTo,
        bool? PreferRecent,
        // Legacy flat fields kept for compatibility with planner system
        // prompts persisted before the generic schema existed.
        string? Vendor,
        string? Product,
        string? Version,
        string? CveId,
        string? RiskFilter,
        int? CveYear);
}
