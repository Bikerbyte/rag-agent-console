using SecurityAdvisoryBot.Models;

namespace SecurityAdvisoryBot.Services;

public interface IAdvisoryVectorStore
{
    Task<IReadOnlyList<AdvisoryVectorSearchCandidate>> SearchAsync(
        AdvisoryVectorSearchRequest request,
        CancellationToken cancellationToken = default);
}
