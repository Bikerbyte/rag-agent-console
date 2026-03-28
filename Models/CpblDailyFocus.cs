namespace CPBLLineBotCloud.Models;

public class CpblDailyFocus
{
    public DateOnly FocusDate { get; set; }
    public IReadOnlyList<string> Items { get; set; } = [];
}
