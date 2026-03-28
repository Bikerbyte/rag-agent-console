using System.ComponentModel.DataAnnotations;

namespace CPBLLineBotCloud.Models;

public class NewsInfo
{
    public int NewsInfoId { get; set; }

    [MaxLength(200)]
    public required string Title { get; set; }

    [MaxLength(64)]
    public required string SourceName { get; set; }

    [MaxLength(300)]
    public required string Url { get; set; }

    public DateTimeOffset PublishTime { get; set; }

    [MaxLength(32)]
    public string? Category { get; set; }

    [MaxLength(600)]
    public string? Summary { get; set; }

    public bool IsSent { get; set; }
}
