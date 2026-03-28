namespace CPBLLineBotCloud.Models;

public class CpblBattingLine
{
    public required string SeasonLabel { get; set; }
    public int Games { get; set; }
    public int PlateAppearances { get; set; }
    public int AtBats { get; set; }
    public int Hits { get; set; }
    public int RunsBattedIn { get; set; }
    public int Runs { get; set; }
    public int Doubles { get; set; }
    public int Triples { get; set; }
    public int HomeRuns { get; set; }
    public int Walks { get; set; }
    public int HitByPitch { get; set; }
    public int SacrificeFlies { get; set; }
    public int Strikeouts { get; set; }
    public int TotalBases { get; set; }
    public decimal Average { get; set; }
    public decimal OnBasePercentage { get; set; }
    public decimal SluggingPercentage { get; set; }
    public decimal Ops { get; set; }
    public decimal OpsPlus { get; set; }
    public decimal WrcPlus { get; set; }
    public decimal Woba { get; set; }
    public decimal Babip { get; set; }
    public decimal WalkPercentage { get; set; }
    public decimal StrikeoutPercentage { get; set; }
}
