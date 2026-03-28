using System.ComponentModel.DataAnnotations;

namespace CPBLLineBotCloud.Models;

public class SyncJobLog
{
    public int SyncJobLogId { get; set; }

    [MaxLength(64)]
    public required string JobName { get; set; }

    public DateTimeOffset StartTime { get; set; }
    public DateTimeOffset? EndTime { get; set; }
    public bool IsSuccess { get; set; }

    [MaxLength(400)]
    public string? Message { get; set; }
}
