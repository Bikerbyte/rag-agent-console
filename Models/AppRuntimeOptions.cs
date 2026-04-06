namespace CPBLLineBotCloud.Models;

/// <summary>
/// 控制目前節點要啟用哪些執行角色，方便同一份程式部署到不同 VM。
/// </summary>
public class AppRuntimeOptions
{
    public const string SectionName = "AppRuntime";

    public string InstanceName { get; set; } = "local-node";
    public bool EnableTelegramWebhookIngress { get; set; } = true;
    public bool EnableTelegramPollingWorker { get; set; } = true;
    public bool EnableTelegramUpdateQueueWorker { get; set; } = true;
    public bool EnableOfficialDataSyncWorker { get; set; } = true;
    public bool EnableNotificationWorker { get; set; } = true;
}
