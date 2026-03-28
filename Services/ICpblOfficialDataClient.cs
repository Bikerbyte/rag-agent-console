using CPBLLineBotCloud.Models;

namespace CPBLLineBotCloud.Services;

public interface ICpblOfficialDataClient
{
    Task<IReadOnlyList<CpblOfficialGameSnapshot>> GetGamesAsync(DateOnly targetDate, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<CpblTeamStandingSnapshot>> GetStandingsAsync(CancellationToken cancellationToken = default);
    Task<CpblPlayerStatsResult?> GetPlayerStatsAsync(string playerName, CancellationToken cancellationToken = default);
    Task<CpblMatchupResult?> GetMatchupAsync(string hitterName, string pitcherName, CancellationToken cancellationToken = default);
}
