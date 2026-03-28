namespace CPBLLineBotCloud.Models;

public class CpblTeamStandingSnapshot
{
    public int Rank { get; set; }
    public required string TeamCode { get; set; }
    public required string TeamName { get; set; }
    public int GamesPlayed { get; set; }
    public int Wins { get; set; }
    public int Losses { get; set; }
    public int Ties { get; set; }
    public decimal WinningPercentage { get; set; }
    public required string GamesBehindText { get; set; }
    public required string StreakText { get; set; }
}
