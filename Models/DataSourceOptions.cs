namespace CPBLLineBotCloud.Models;

public class DataSourceOptions
{
    public const string SectionName = "DataSources";

    public int AutoSyncIntervalMinutes { get; set; } = 15;
}
