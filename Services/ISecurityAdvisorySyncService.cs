using SecurityAdvisoryBot.Models;

namespace SecurityAdvisoryBot.Services;

public interface ISecurityAdvisorySyncService
{
    Task<SecurityAdvisorySyncResult> SyncAsync(CancellationToken cancellationToken = default);
}
