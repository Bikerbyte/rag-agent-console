using System.ComponentModel.DataAnnotations;

namespace CPBLLineBotCloud.Models;

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
