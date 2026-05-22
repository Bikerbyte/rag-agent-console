using SecurityAdvisoryBot.Models;

namespace SecurityAdvisoryBot.Services;

public class ConfiguredAdvisoryVectorStore(
    EfAdvisoryVectorStore efStore,
    PgVectorAdvisoryVectorStore pgVectorStore,
    IAppSettingsService appSettingsService,
    ILogger<ConfiguredAdvisoryVectorStore> logger) : IAdvisoryVectorStore
{
    public async Task<IReadOnlyList<AdvisoryVectorSearchCandidate>> SearchAsync(
        AdvisoryVectorSearchRequest request,
        CancellationToken cancellationToken = default)
    {
        var options = await appSettingsService.GetVectorStoreOptionsAsync(cancellationToken);
        var provider = options.Provider;
        if (string.Equals(provider, VectorStoreProviderNames.PgVector, StringComparison.OrdinalIgnoreCase) &&
            string.IsNullOrWhiteSpace(request.CveId))
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
