using System.ComponentModel.DataAnnotations;

namespace SecurityAdvisoryBot.Models;

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

public class SyncJobLog
{
    public int SyncJobLogId { get; set; }

    [MaxLength(128)]
    public string? InstanceName { get; set; }

    [MaxLength(64)]
    public required string JobName { get; set; }

    public DateTimeOffset StartTime { get; set; }
    public DateTimeOffset? EndTime { get; set; }
    public bool IsSuccess { get; set; }

    [MaxLength(400)]
    public string? Message { get; set; }
}

public class RuntimeLeadershipLease
{
    [MaxLength(128)]
    public required string LeaseName { get; set; }

    [MaxLength(128)]
    public required string OwnerInstanceName { get; set; }

    public DateTimeOffset AcquiredAt { get; set; }
    public DateTimeOffset RenewedAt { get; set; }
    public DateTimeOffset ExpiresAt { get; set; }
    public DateTimeOffset LastUpdatedAt { get; set; }
}

public class RuntimeLeadershipLeaseResult
{
    public required string LeaseName { get; init; }
    public required string InstanceName { get; init; }
    public required string CurrentOwnerInstanceName { get; init; }
    public DateTimeOffset ExpiresAt { get; init; }
    public bool IsLeader { get; init; }
    public required string Action { get; init; }
}

public class RuntimeNodeHeartbeat
{
    public int RuntimeNodeHeartbeatId { get; set; }

    [MaxLength(128)]
    public required string InstanceName { get; set; }

    [MaxLength(128)]
    public required string MachineName { get; set; }

    [MaxLength(64)]
    public required string EnvironmentName { get; set; }

    [MaxLength(400)]
    public required string RoleSummary { get; set; }

    [MaxLength(32)]
    public string Status { get; set; } = "Online";

    public int ProcessId { get; set; }
    public DateTimeOffset ProcessStartedTime { get; set; }
    public DateTimeOffset LastSeenTime { get; set; }

    [MaxLength(64)]
    public string? AppVersion { get; set; }
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
