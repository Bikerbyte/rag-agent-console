using CPBLLineBotCloud.Models;

namespace CPBLLineBotCloud.Services;

public interface ISecurityAdvisorySource
{
    string SourceName { get; }

    Task<IReadOnlyList<SecurityAdvisoryCandidate>> FetchLatestAsync(CancellationToken cancellationToken = default);
}
