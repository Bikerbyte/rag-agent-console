using System.ComponentModel.DataAnnotations;

namespace SecurityAdvisoryBot.Models;

public class TelegramChatSubscription
{
    public int TelegramChatSubscriptionId { get; set; }

    [MaxLength(64)]
    public required string ChatId { get; set; }

    [MaxLength(128)]
    public required string ChatTitle { get; set; }

    public bool EnableAdvisoryPush { get; set; }

    [MaxLength(800)]
    public string? AdvisoryKeywords { get; set; }

    [MaxLength(32)]
    public string? MinimumSeverity { get; set; }

    public DateTimeOffset CreatedTime { get; set; }
    public DateTimeOffset LastUpdatedTime { get; set; }
}
