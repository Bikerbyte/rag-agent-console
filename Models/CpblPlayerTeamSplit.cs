namespace CPBLLineBotCloud.Models;

public class CpblPlayerTeamSplit
{
    public required string YearLabel { get; set; }
    public required string TeamCode { get; set; }
    public required string TeamName { get; set; }
    public int Games { get; set; }
    public int PlateAppearances { get; set; }
    public int Hits { get; set; }
    public int HomeRuns { get; set; }
    public int RunsBattedIn { get; set; }
    public decimal Average { get; set; }
    public decimal OnBasePercentage { get; set; }
    public decimal SluggingPercentage { get; set; }
    public decimal Ops { get; set; }
}
