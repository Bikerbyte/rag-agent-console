using RagAgentConsole.Models;

namespace RagAgentConsole.Services;

public interface IAdvisoryVectorStore
{
    Task<IReadOnlyList<AdvisoryVectorSearchCandidate>> SearchAsync(
        AdvisoryVectorSearchRequest request,
        CancellationToken cancellationToken = default);
}
