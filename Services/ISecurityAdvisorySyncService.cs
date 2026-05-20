using CPBLLineBotCloud.Models;

namespace CPBLLineBotCloud.Services;

public interface ISecurityAdvisorySyncService
{
    Task<SecurityAdvisorySyncResult> SyncAsync(CancellationToken cancellationToken = default);
}
