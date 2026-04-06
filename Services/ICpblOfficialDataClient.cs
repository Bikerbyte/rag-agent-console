using CPBLLineBotCloud.Models;

namespace CPBLLineBotCloud.Services;

/// <summary>
/// 讀取官方 CPBL 資料的輕量 client，處理那些不一定會落地保存的查詢。
/// </summary>
public interface ICpblOfficialDataClient
{
    Task<IReadOnlyList<CpblOfficialGameSnapshot>> GetGamesAsync(DateOnly targetDate, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<CpblTeamStandingSnapshot>> GetStandingsAsync(CancellationToken cancellationToken = default);
    Task<CpblPlayerStatsResult?> GetPlayerStatsAsync(string playerName, CancellationToken cancellationToken = default);
    Task<CpblMatchupResult?> GetMatchupAsync(string hitterName, string pitcherName, CancellationToken cancellationToken = default);
}
