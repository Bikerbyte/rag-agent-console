using RagAgentConsole.Models;

namespace RagAgentConsole.Services;

public interface IRagVectorStore
{
    Task<IReadOnlyList<RetrievalCandidate>> SearchAsync(
        RetrievalRequest request,
        CancellationToken cancellationToken = default);
}
