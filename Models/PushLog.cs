using System.ComponentModel.DataAnnotations;

namespace CPBLLineBotCloud.Models;

public class PushLog
{
    public int PushLogId { get; set; }

    [MaxLength(128)]
    public string? InstanceName { get; set; }

    [MaxLength(32)]
    public required string PushType { get; set; }

    [MaxLength(128)]
    public required string TargetGroupId { get; set; }

    [MaxLength(200)]
    public required string MessageTitle { get; set; }

    public bool IsSuccess { get; set; }

    [MaxLength(400)]
    public string? ErrorMessage { get; set; }

    public DateTimeOffset CreatedTime { get; set; }
}
