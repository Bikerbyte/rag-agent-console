namespace CPBLLineBotCloud.Services;

/// <summary>
/// 將 CPBL 官方賽程與比分資料同步到本機資料庫。
/// </summary>
public interface ICpblGameSyncService
{
    /// <summary>
    /// 同步目前台北時間對應的比賽日期資料。
    /// </summary>
    Task<int> SyncAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// 針對指定日期同步官方賽程資料。
    /// </summary>
    Task<int> SyncDateAsync(DateOnly targetDate, CancellationToken cancellationToken = default);
}
