namespace CPBLLineBotCloud.Models;

public class DataSourceOptions
{
    public const string SectionName = "DataSources";

    public string CpblScheduleBaseUrl { get; set; } = "https://www.cpbl.com.tw";
    public string BaseballNewsBaseUrl { get; set; } = "https://www.cpbl.com.tw/xmdoc";
    public int AutoSyncIntervalMinutes { get; set; } = 15;
}
