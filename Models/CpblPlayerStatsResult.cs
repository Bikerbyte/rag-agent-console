namespace CPBLLineBotCloud.Models;

public class CpblPlayerStatsResult
{
    public required CpblPlayerProfile Profile { get; set; }
    public CpblBattingLine? LatestBatting { get; set; }
    public CpblBattingLine? CareerBatting { get; set; }
    public CpblPitchingLine? LatestPitching { get; set; }
    public CpblPitchingLine? CareerPitching { get; set; }
    public IReadOnlyList<CpblPlayerTeamSplit> TeamSplits { get; set; } = [];

    public bool IsPitcherPrimary =>
        Profile.Position?.Contains("投手", StringComparison.Ordinal) == true ||
        (LatestPitching is not null && LatestPitching.Games > 0 && (LatestBatting is null || LatestBatting.PlateAppearances <= 5));
}
