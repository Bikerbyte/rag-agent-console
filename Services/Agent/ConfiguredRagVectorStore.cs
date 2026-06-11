using RagAgentConsole.Models;

namespace RagAgentConsole.Services;

public interface IRagVectorStore
{
    Task<IReadOnlyList<RetrievalCandidate>> SearchAsync(
        RetrievalRequest request,
        CancellationToken cancellationToken = default);
}

public class ConfiguredRagVectorStore(
    EfRagVectorStore efStore,
    PgVectorRagVectorStore pgVectorStore,
    IAppSettingsService appSettingsService,
    ILogger<ConfiguredRagVectorStore> logger) : IRagVectorStore
{
    public async Task<IReadOnlyList<RetrievalCandidate>> SearchAsync(
        RetrievalRequest request,
        CancellationToken cancellationToken = default)
    {
        var options = await appSettingsService.GetVectorStoreOptionsAsync(cancellationToken);
        var provider = options.Provider;

        // Date / exact-id constraints need the EF store, which can apply them
        // as SQL predicates; pgvector ordering alone cannot guarantee the
        // strictly filtered candidates survive the top-K cut.
        var advisoryFilter = SecurityAdvisoryFilter.From(request);
        if (request.PublishedFrom.HasValue || request.PublishedTo.HasValue || advisoryFilter.CveYear.HasValue)
        {
            return await efStore.SearchAsync(request, cancellationToken);
        }

        if (string.Equals(provider, VectorStoreProviderNames.PgVector, StringComparison.OrdinalIgnoreCase) &&
            string.IsNullOrWhiteSpace(advisoryFilter.CveId))
        {
            try
            {
                var candidates = await pgVectorStore.SearchAsync(request, cancellationToken);
                if (candidates.Count > 0 || !options.UseJsonFallback)
                {
                    return candidates;
                }

                logger.LogInformation("PgVector returned no candidates. Falling back to EF JSON vector search.");
            }
            catch (PgVectorUnavailableException exception) when (options.UseJsonFallback)
            {
                logger.LogWarning(exception, "PgVector vector store is unavailable. Falling back to EF JSON vector search.");
            }
        }

        return await efStore.SearchAsync(request, cancellationToken);
    }
}
