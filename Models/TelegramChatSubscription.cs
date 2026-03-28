using System.ComponentModel.DataAnnotations;

namespace CPBLLineBotCloud.Models;

public class TelegramChatSubscription
{
    public int TelegramChatSubscriptionId { get; set; }

    [MaxLength(64)]
    public required string ChatId { get; set; }

    [MaxLength(128)]
    public required string ChatTitle { get; set; }

    public bool EnableSchedulePush { get; set; }
    public bool EnableNewsPush { get; set; }

    [MaxLength(16)]
    public string? FollowedTeamCode { get; set; }

    public DateTimeOffset CreatedTime { get; set; }
    public DateTimeOffset LastUpdatedTime { get; set; }
}
