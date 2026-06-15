using System.Text.Json;
using System.Text.RegularExpressions;
using RagAgentConsole.Models;

namespace RagAgentConsole.Services;

public interface IRagQueryPlanner
{
    Task<RetrievalPlan> BuildPlanAsync(
        string question,
        IReadOnlyList<AgentConversationMessage>? history = null,
        CancellationToken cancellationToken = default);
}

public class RagQueryPlanner(
    IAiChatClient aiChatClient,
    IAppSettingsService appSettingsService,
    ITokenizer tokenizer,
    ILogger<RagQueryPlanner> logger) : IRagQueryPlanner
{
    public async Task<RetrievalPlan> BuildPlanAsync(
        string question,
        IReadOnlyList<AgentConversationMessage>? history = null,
        CancellationToken cancellationToken = default)
    {
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

        return await FallbackPlanAsync(question, cancellationToken);
    }

    private async Task<RetrievalPlan> BuildWithAiAsync(
        string question,
        IReadOnlyList<AgentConversationMessage>? history,
        CancellationToken cancellationToken)
    {
        var agentOptions = await appSettingsService.GetAgentOptionsAsync(cancellationToken);
        var userPrompt = $$"""
        Conversation history:
        {{BuildHistoryText(history)}}

        User question:
        {{question}}

        Expected JSON shape:
        {
          "intent": "knowledge_lookup",
          "moduleName": "InternalDocs",
          "retrievalQuery": "remote work approval policy",
          "searchKeywords": ["remote", "work", "approval"],
          "entities": {
            "policyType": "remote work"
          },
          "filters": {},
          "notes": []
        }
        """;

        var response = await aiChatClient.CompleteAsync(
            agentOptions.PlannerSystemPrompt,
            userPrompt,
            cancellationToken);
        if (string.IsNullOrWhiteSpace(response))
        {
            logger.LogWarning("AI planner returned empty response; using raw question as retrieval query.");
            return await FallbackPlanAsync(question, cancellationToken);
        }

        try
        {
            var dto = JsonSerializer.Deserialize<QueryPlanDto>(StripJsonFence(response), JsonOptions);
            if (dto is null)
            {
                return await FallbackPlanAsync(question, cancellationToken);
            }

            var plan = new RetrievalPlan(
                question,
                string.IsNullOrWhiteSpace(dto.RetrievalQuery) ? question.Trim() : dto.RetrievalQuery.Trim(),
                NormalizeNullable(dto.Intent),
                NormalizeKeywords(dto.SearchKeywords),
                dto.Notes?.Where(note => !string.IsNullOrWhiteSpace(note)).Select(note => note.Trim()).ToList() ?? [],
                NormalizeValues(dto.Entities),
                NormalizeValues(dto.Filters),
                ResolveModuleName(dto.ModuleName, agentOptions.DefaultModule),
                PlannerStrategy.Ai);

            return plan;
        }
        catch (JsonException exception)
        {
            logger.LogWarning(exception, "AI planner returned invalid JSON; using raw question as retrieval query.");
            return await FallbackPlanAsync(question, cancellationToken);
        }
    }

    private async Task<RetrievalPlan> FallbackPlanAsync(string question, CancellationToken cancellationToken)
    {
        var defaultModule = KnowledgeModuleNames.InternalDocs;
        try
        {
            defaultModule = (await appSettingsService.GetAgentOptionsAsync(cancellationToken)).DefaultModule;
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            logger.LogDebug(exception, "Could not load agent options for fallback plan; using InternalDocs.");
        }

        return new RetrievalPlan(
            question,
            question.Trim(),
            "knowledge_lookup",
            NormalizeKeywords(tokenizer.Tokenize(question)),
            [],
            RetrievalPlan.EmptyValues,
            RetrievalPlan.EmptyValues,
            KnowledgeModuleNames.Normalize(defaultModule),
            PlannerStrategy.RawFallback);
    }

    internal static string BuildHistoryText(IReadOnlyList<AgentConversationMessage>? history)
        => history is null || history.Count == 0
            ? "(none)"
            : string.Join(Environment.NewLine, history.TakeLast(6).Select(item => $"{item.Role}: {item.Content}"));

    internal static IReadOnlyList<string> NormalizeKeywords(IEnumerable<string>? keywords)
        => (keywords ?? [])
            .Select(keyword => keyword.Trim().ToLowerInvariant())
            .Where(keyword => !string.IsNullOrWhiteSpace(keyword))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(8)
            .ToList();

    private static string ResolveModuleName(string? moduleName, string fallback)
    {
        if (string.Equals(moduleName, KnowledgeModuleNames.WorkflowQa, StringComparison.OrdinalIgnoreCase))
        {
            return KnowledgeModuleNames.WorkflowQa;
        }

        if (string.Equals(moduleName, KnowledgeModuleNames.InternalDocs, StringComparison.OrdinalIgnoreCase))
        {
            return KnowledgeModuleNames.InternalDocs;
        }

        return KnowledgeModuleNames.Normalize(fallback);
    }

    private static IReadOnlyDictionary<string, string?> NormalizeValues(
        IReadOnlyDictionary<string, JsonElement>? values)
    {
        if (values is null)
        {
            return RetrievalPlan.EmptyValues;
        }

        var normalized = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
        foreach (var (key, element) in values)
        {
            if (string.IsNullOrWhiteSpace(key))
            {
                continue;
            }

            var value = element.ValueKind switch
            {
                JsonValueKind.String => element.GetString(),
                JsonValueKind.Number => element.GetRawText(),
                JsonValueKind.True => "true",
                JsonValueKind.False => "false",
                _ => null
            };

            if (!string.IsNullOrWhiteSpace(value) &&
                !string.Equals(value, "none", StringComparison.OrdinalIgnoreCase))
            {
                normalized[key.Trim()] = value.Trim();
            }
        }

        return normalized;
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

    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };

    private sealed record QueryPlanDto(
        string? Intent,
        string? ModuleName,
        string? RetrievalQuery,
        IReadOnlyList<string>? SearchKeywords,
        IReadOnlyDictionary<string, JsonElement>? Entities,
        IReadOnlyDictionary<string, JsonElement>? Filters,
        IReadOnlyList<string>? Notes);
}
