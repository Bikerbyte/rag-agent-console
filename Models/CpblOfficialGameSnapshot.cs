namespace CPBLLineBotCloud.Models;

public class CpblOfficialGameSnapshot
{
    public DateOnly GameDate { get; set; }
    public TimeOnly? StartTime { get; set; }
    public required string AwayTeamCode { get; set; }
    public required string AwayTeamName { get; set; }
    public required string HomeTeamCode { get; set; }
    public required string HomeTeamName { get; set; }
    public int? AwayScore { get; set; }
    public int? HomeScore { get; set; }
    public required string Status { get; set; }
    public string? InningText { get; set; }
    public string? Venue { get; set; }
    public string? VodUrl { get; set; }
    public string? LiveUrl { get; set; }
    public string? WinningPitcherName { get; set; }
    public string? LosingPitcherName { get; set; }
    public int VideoCount { get; set; }
    public int NewsCount { get; set; }
    public int? AwayWins { get; set; }
    public int? AwayLosses { get; set; }
    public int? AwayTies { get; set; }
    public int? HomeWins { get; set; }
    public int? HomeLosses { get; set; }
    public int? HomeTies { get; set; }
}
