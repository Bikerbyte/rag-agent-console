namespace CPBLLineBotCloud.Services;

public interface IBaseballNewsSyncService
{
    Task<int> SyncAsync(CancellationToken cancellationToken = default);
}
