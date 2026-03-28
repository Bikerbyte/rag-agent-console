namespace CPBLLineBotCloud.Models;

public class CpblScorePrediction
{
    public required string AwayTeamCode { get; set; }
    public required string AwayTeamName { get; set; }
    public required string HomeTeamCode { get; set; }
    public required string HomeTeamName { get; set; }
    public decimal AwayExpectedRuns { get; set; }
    public decimal HomeExpectedRuns { get; set; }
    public required string ConfidenceLabel { get; set; }
    public required string Insight { get; set; }
}
