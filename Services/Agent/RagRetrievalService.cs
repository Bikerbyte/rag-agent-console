using RagAgentConsole.Models;

namespace RagAgentConsole.Services;

public interface IRagRetrievalService
{
    Task<IReadOnlyList<RetrievalResult>> SearchAsync(
        string question,
        int maxResults = 5,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<RetrievalResult>> SearchAsync(
        string question,
        IReadOnlyList<AgentConversationMessage>? history,
        int maxResults = 5,
        CancellationToken cancellationToken = default);

    Task<RetrievalResponse> SearchWithTraceAsync(
        string question,
        IReadOnlyList<AgentConversationMessage>? history = null,
        int maxResults = 5,
        string? moduleName = null,
        string retrievalMode = RetrievalModes.Hybrid,
        CancellationToken cancellationToken = default);
}

public class RagRetrievalService(
    IRagEmbeddingService embeddingService,
    IRagVectorStore vectorStore,
    IRagQueryPlanner queryPlanner,
    IAppSettingsService appSettingsService,
    ILogger<RagRetrievalService> logger) : IRagRetrievalService
{
    public async Task<IReadOnlyList<RetrievalResult>> SearchAsync(
        string question,
        int maxResults = 5,
        CancellationToken cancellationToken = default)
        => await SearchAsync(question, history: null, maxResults, cancellationToken);

    public async Task<IReadOnlyList<RetrievalResult>> SearchAsync(
        string question,
        IReadOnlyList<AgentConversationMessage>? history,
        int maxResults = 5,
        CancellationToken cancellationToken = default)
        => (await SearchWithTraceAsync(question, history, maxResults, cancellationToken: cancellationToken)).Results;

    public async Task<RetrievalResponse> SearchWithTraceAsync(
        string question,
        IReadOnlyList<AgentConversationMessage>? history = null,
        int maxResults = 5,
        string? moduleName = null,
        string retrievalMode = RetrievalModes.Hybrid,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(question))
        {
            return new RetrievalResponse(
                new RetrievalPlan(
                    question,
                    string.Empty,
                    null,
                    [],
                    [],
                    RetrievalPlan.EmptyValues,
                    RetrievalPlan.EmptyValues,
                    moduleName ?? KnowledgeModuleNames.InternalDocs),
                RetrievalModes.Normalize(retrievalMode),
                []);
        }

        var plan = await queryPlanner.BuildPlanAsync(question, history, cancellationToken);
        var effectiveModule = string.IsNullOrWhiteSpace(moduleName) ? plan.ModuleName : moduleName.Trim();
        var effectiveMode = RetrievalModes.Normalize(retrievalMode);
        var queryVector = await embeddingService.BuildEmbeddingAsync(plan.RetrievalQuery, cancellationToken);
        var ragOptions = await appSettingsService.GetRagOptionsAsync(cancellationToken);
        var effectiveMax = Math.Clamp(maxResults, 1, Math.Max(1, ragOptions.MaxChunks));
        var request = new RetrievalRequest(
            plan.RetrievalQuery,
            plan.SearchKeywords,
            plan.Filters,
            queryVector,
            effectiveMax,
            effectiveModule,
            effectiveMode);
        var candidates = await vectorStore.SearchAsync(request, cancellationToken);

        var ranked = new List<RetrievalResult>();
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

            ranked.Add(new RetrievalResult(
                candidate.Document,
                candidate.ChunkText,
                score,
                vectorScore,
                candidate.TextScore));
        }

        logger.LogDebug(
            "RAG search produced {CandidateCount} candidates and {RankedCount} ranked results. RetrievalQuery={RetrievalQuery}.",
            candidates.Count,
            ranked.Count,
            plan.RetrievalQuery);

        var results = ranked
            .OrderByDescending(item => item.Score)
            .Take(effectiveMax)
            .ToList();

        return new RetrievalResponse(
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
