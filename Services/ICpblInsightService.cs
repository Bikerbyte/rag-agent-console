using CPBLLineBotCloud.Models;

namespace CPBLLineBotCloud.Services;

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
