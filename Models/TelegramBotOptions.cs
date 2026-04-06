namespace CPBLLineBotCloud.Models;

public class TelegramBotOptions
{
    public const string SectionName = "TelegramBot";

    public bool Enabled { get; set; }
    public string BotToken { get; set; } = string.Empty;
    public string ApiBaseUrl { get; set; } = "https://api.telegram.org";
    public int PollingDelaySeconds { get; set; } = 3;
    public bool UseWebhookMode { get; set; }
    public string WebhookPath { get; set; } = "/api/telegram/webhook";
    public string WebhookUrl { get; set; } = string.Empty;
    public string WebhookSecretToken { get; set; } = string.Empty;
}
