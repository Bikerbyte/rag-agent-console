namespace CPBLLineBotCloud.Models;

public class CpblGameRecommendation
{
    public DateOnly FocusDate { get; set; }
    public required string GameLabel { get; set; }
    public required string StartTimeText { get; set; }
    public required string VenueText { get; set; }
    public required IReadOnlyList<string> Reasons { get; set; }
}
