namespace CPBLLineBotCloud.Models;

public class CpblTeamSummary
{
    public required string TeamCode { get; set; }
    public required string TeamName { get; set; }
    public CpblTeamStandingSnapshot? Standing { get; set; }
    public int RecentGamesCount { get; set; }
    public int RecentWins { get; set; }
    public int RecentLosses { get; set; }
    public decimal RecentRunsScoredAverage { get; set; }
    public decimal RecentRunsAllowedAverage { get; set; }
    public int RecentHomeWins { get; set; }
    public int RecentHomeLosses { get; set; }
    public int RecentAwayWins { get; set; }
    public int RecentAwayLosses { get; set; }
    public string RecentTrendText { get; set; } = string.Empty;
    public GameInfo? LatestGame { get; set; }
    public GameInfo? NextGame { get; set; }
    public string? LatestNewsTitle { get; set; }
}
