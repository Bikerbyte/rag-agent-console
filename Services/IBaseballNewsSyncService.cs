namespace CPBLLineBotCloud.Services;

/// <summary>
/// 抓取最新 CPBL 新聞並寫入本機資料，供管理頁面和推播流程使用。
/// </summary>
public interface IBaseballNewsSyncService
{
    Task<int> SyncAsync(CancellationToken cancellationToken = default);
}
