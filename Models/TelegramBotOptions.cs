namespace CPBLLineBotCloud.Models;

public class TelegramBotOptions
{
    public const string SectionName = "TelegramBot";

    public bool Enabled { get; set; }
    public string BotToken { get; set; } = string.Empty;
    public string ApiBaseUrl { get; set; } = "https://api.telegram.org";
    public int PollingDelaySeconds { get; set; } = 3;
}
