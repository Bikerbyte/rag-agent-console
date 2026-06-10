using RagAgentConsole.Models;
using Microsoft.Extensions.Options;

namespace RagAgentConsole.Services;

public class RagRetrievalService(
    IRagEmbeddingService embeddingService,
    IRagVectorStore vectorStore,
    IRagQueryPlanner queryPlanner,
    IOptions<SecurityAdvisoryOptions> options,
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
                    moduleName ?? KnowledgeModuleNames.CveAdvisory),
                RetrievalModes.Normalize(retrievalMode),
                []);
        }

        var plan = await queryPlanner.BuildPlanAsync(question, history, cancellationToken);
        var effectiveModule = string.IsNullOrWhiteSpace(moduleName) ? plan.ModuleName : moduleName.Trim();
        var effectiveMode = RetrievalModes.Normalize(retrievalMode);
        var queryVector = await embeddingService.BuildEmbeddingAsync(plan.RetrievalQuery, cancellationToken);
        var effectiveMax = Math.Clamp(maxResults, 1, Math.Max(1, options.Value.RagMaxChunks));
        var request = new RetrievalRequest(
            plan.RetrievalQuery,
            plan.SearchKeywords,
            plan.Entities,
            plan.Filters,
            queryVector,
            effectiveMax,
            effectiveModule,
            effectiveMode,
            plan.PublishedFrom,
            plan.PublishedTo,
            plan.PreferRecent);
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

            ranked.Add(candidate switch
            {
                AdvisoryCandidate a => new RetrievalResult(a.Advisory, null, a.ChunkText, score, vectorScore, a.TextScore),
                DocumentCandidate d => new RetrievalResult(null, d.Document, d.ChunkText, score, vectorScore, d.TextScore),
                _ => throw new InvalidOperationException($"Unexpected candidate type: {candidate.GetType().Name}")
            });
        }

        logger.LogDebug(
            "RAG search produced {CandidateCount} candidates and {RankedCount} ranked results. RetrievalQuery={RetrievalQuery}.",
            candidates.Count,
            ranked.Count,
            plan.RetrievalQuery);

        var ordered = plan.PreferRecent
            ? ranked.OrderByDescending(item => item.Advisory?.PublishedAt ?? DateTimeOffset.MinValue)
                .ThenByDescending(item => item.Score)
            : ranked.OrderByDescending(item => item.Score);

        var results = ordered
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
