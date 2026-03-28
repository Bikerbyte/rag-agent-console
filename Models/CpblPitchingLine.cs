namespace CPBLLineBotCloud.Models;

public class CpblPitchingLine
{
    public required string SeasonLabel { get; set; }
    public int Games { get; set; }
    public int Starts { get; set; }
    public int Wins { get; set; }
    public int Losses { get; set; }
    public int Saves { get; set; }
    public int Strikeouts { get; set; }
    public int Walks { get; set; }
    public int HitsAllowed { get; set; }
    public int EarnedRuns { get; set; }
    public decimal InningsPitched { get; set; }
    public decimal Era { get; set; }
    public decimal Whip { get; set; }
    public decimal KPerNine { get; set; }
    public decimal BPerNine { get; set; }
    public decimal HPerNine { get; set; }
    public decimal Fip { get; set; }
    public decimal EraPlus { get; set; }
    public decimal WalkPercentage { get; set; }
    public decimal StrikeoutPercentage { get; set; }
}
