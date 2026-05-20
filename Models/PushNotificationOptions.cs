namespace CPBLLineBotCloud.Models;

public class PushNotificationOptions
{
    public const string SectionName = "PushNotifications";

    public bool Enabled { get; set; } = true;
    public bool EnableSecurityAdvisoryPush { get; set; } = true;
    public int WorkerIntervalSeconds { get; set; } = 90;
    public int AdvisoryLookbackHours { get; set; } = 72;
}
