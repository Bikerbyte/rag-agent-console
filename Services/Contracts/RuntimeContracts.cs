using SecurityAdvisoryBot.Models;

namespace SecurityAdvisoryBot.Services;

public interface IRuntimeLeadershipLeaseService
{
    Task<RuntimeLeadershipLeaseResult> TryAcquireOrRenewAsync(string leaseName, CancellationToken cancellationToken = default);
    Task<bool> IsCurrentLeaderAsync(string leaseName, CancellationToken cancellationToken = default);
    Task ReleaseIfOwnedAsync(string leaseName, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<RuntimeLeadershipLease>> GetActiveLeasesAsync(CancellationToken cancellationToken = default);
}
