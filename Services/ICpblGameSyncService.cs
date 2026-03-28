namespace CPBLLineBotCloud.Services;

public interface ICpblGameSyncService
{
    Task<int> SyncAsync(CancellationToken cancellationToken = default);
    Task<int> SyncDateAsync(DateOnly targetDate, CancellationToken cancellationToken = default);
}
