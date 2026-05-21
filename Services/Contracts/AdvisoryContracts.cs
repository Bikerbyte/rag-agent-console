using SecurityAdvisoryBot.Models;

namespace SecurityAdvisoryBot.Services;

public interface ISecurityAdvisorySource
{
    string SourceName { get; }

    Task<IReadOnlyList<SecurityAdvisoryCandidate>> FetchLatestAsync(CancellationToken cancellationToken = default);
}

public interface ISecurityAdvisorySyncService
{
    Task<SecurityAdvisorySyncResult> SyncAsync(CancellationToken cancellationToken = default);
}

public interface ITelegramNotificationDispatchService
{
    Task ProcessPendingNotificationsAsync(CancellationToken cancellationToken = default);
}
