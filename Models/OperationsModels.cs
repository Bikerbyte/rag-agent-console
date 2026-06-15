using System.ComponentModel.DataAnnotations;

namespace RagAgentConsole.Models;

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

public class AppSetting
{
    public int AppSettingId { get; set; }
    public required string SettingKey { get; set; }
    public string? SettingValue { get; set; }
    public bool IsSecret { get; set; }
    public DateTimeOffset UpdatedTime { get; set; }
}

public class TelegramUpdateInbox
{
    public int TelegramUpdateInboxId { get; set; }

    public long UpdateId { get; set; }

    [MaxLength(32)]
    public required string SourceType { get; set; }

    [MaxLength(32)]
    public required string Status { get; set; }

    public required string PayloadJson { get; set; }

    [MaxLength(128)]
    public string? IngressNode { get; set; }

    [MaxLength(128)]
    public string? ProcessingNode { get; set; }

    public int AttemptCount { get; set; }
    public DateTimeOffset EnqueuedTime { get; set; }
    public DateTimeOffset? ProcessingStartedTime { get; set; }
    public DateTimeOffset? LeaseUntil { get; set; }
    public DateTimeOffset? ProcessedTime { get; set; }
    public DateTimeOffset LastUpdatedTime { get; set; }

    [MaxLength(500)]
    public string? LastError { get; set; }
}
