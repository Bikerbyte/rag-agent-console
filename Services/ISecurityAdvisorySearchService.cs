using CPBLLineBotCloud.Models;

namespace CPBLLineBotCloud.Services;

public interface ISecurityAdvisorySearchService
{
    Task<IReadOnlyList<SecurityAdvisorySearchResult>> SearchAsync(
        string question,
        int maxResults = 5,
        CancellationToken cancellationToken = default);
}
