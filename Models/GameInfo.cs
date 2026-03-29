using System.ComponentModel.DataAnnotations;

namespace CPBLLineBotCloud.Models;

public class GameInfo
{
    public int GameInfoId { get; set; }
    public DateOnly GameDate { get; set; }
    public TimeOnly? StartTime { get; set; }

    [MaxLength(16)]
    public required string HomeTeamCode { get; set; }

    [MaxLength(16)]
    public required string AwayTeamCode { get; set; }

    public int? HomeScore { get; set; }
    public int? AwayScore { get; set; }

    public int? PreviousHomeScore { get; set; }
    public int? PreviousAwayScore { get; set; }

    [MaxLength(32)]
    public required string Status { get; set; }

    [MaxLength(32)]
    public string? PreviousStatus { get; set; }

    [MaxLength(32)]
    public string? InningText { get; set; }

    [MaxLength(128)]
    public string? Venue { get; set; }

    public DateTimeOffset LastUpdatedTime { get; set; }
}
