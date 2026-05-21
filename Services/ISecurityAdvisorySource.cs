using SecurityAdvisoryBot.Models;

namespace SecurityAdvisoryBot.Services;

public interface ISecurityAdvisorySource
{
    string SourceName { get; }

    Task<IReadOnlyList<SecurityAdvisoryCandidate>> FetchLatestAsync(CancellationToken cancellationToken = default);
}
