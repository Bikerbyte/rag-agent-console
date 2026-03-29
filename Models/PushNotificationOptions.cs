namespace CPBLLineBotCloud.Models;

public class PushNotificationOptions
{
    public const string SectionName = "PushNotifications";

    public bool Enabled { get; set; } = true;
    public bool EnableDailySummary { get; set; } = true;
    public bool EnableGameStartPush { get; set; } = true;
    public bool EnableLiveScorePush { get; set; } = true;
    public bool EnableNewsPush { get; set; } = true;
    public bool EnableGameFinalPush { get; set; } = true;
    public int WorkerIntervalSeconds { get; set; } = 90;
    public int DailySummaryHour { get; set; } = 11;
    public int DailySummaryMinute { get; set; } = 0;
    public int GameStartLeadMinutes { get; set; } = 30;
    public int NewsLookbackHours { get; set; } = 36;
    public int FinalLookbackHours { get; set; } = 18;
}
