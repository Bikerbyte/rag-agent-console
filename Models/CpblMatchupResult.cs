namespace CPBLLineBotCloud.Models;

public class CpblMatchupResult
{
    public required string HitterName { get; set; }
    public required string PitcherName { get; set; }
    public string? HitterTeamName { get; set; }
    public string? PitcherTeamName { get; set; }
    public int PlateAppearances { get; set; }
    public int Hits { get; set; }
    public int HomeRuns { get; set; }
    public int RunsBattedIn { get; set; }
    public int Walks { get; set; }
    public int Strikeouts { get; set; }
    public decimal Average { get; set; }
    public decimal OnBasePercentage { get; set; }
    public decimal SluggingPercentage { get; set; }
    public decimal Ops { get; set; }
}
