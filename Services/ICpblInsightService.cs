using CPBLLineBotCloud.Models;

namespace CPBLLineBotCloud.Services;

/// <summary>
/// 以同步資料和官方即時資料為基礎，整理較高層的摘要、推薦與洞察結果。
/// </summary>
public interface ICpblInsightService
{
    Task<CpblPlayerStatsResult?> GetPlayerStatsAsync(string playerName, CancellationToken cancellationToken = default);
    Task<CpblMatchupResult?> GetMatchupAsync(string hitterName, string pitcherName, CancellationToken cancellationToken = default);
    Task<CpblTeamSummary?> GetTeamSummaryAsync(string teamCode, CancellationToken cancellationToken = default);
    Task<CpblDailyFocus> GetDailyFocusAsync(CancellationToken cancellationToken = default);
    Task<CpblGameRecommendation?> GetTodayBestGameAsync(CancellationToken cancellationToken = default);
    Task<IReadOnlyList<CpblOfficialGameSnapshot>> GetRecentHighlightsAsync(int lookbackDays, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<CpblScorePrediction>> GetPredictionsAsync(DateOnly targetDate, string? awayTeamCode = null, string? homeTeamCode = null, CancellationToken cancellationToken = default);
}
