using System.Text.Json.Serialization;

namespace CPBLLineBotCloud.Models;

public class TelegramBotProfileResponse
{
    [JsonPropertyName("ok")]
    public bool Ok { get; set; }

    [JsonPropertyName("result")]
    public TelegramBotProfile? Result { get; set; }
}

public class TelegramBotProfile
{
    [JsonPropertyName("id")]
    public long Id { get; set; }

    [JsonPropertyName("is_bot")]
    public bool IsBot { get; set; }

    [JsonPropertyName("first_name")]
    public string? FirstName { get; set; }

    [JsonPropertyName("username")]
    public string? Username { get; set; }

    [JsonPropertyName("can_join_groups")]
    public bool? CanJoinGroups { get; set; }

    [JsonPropertyName("can_read_all_group_messages")]
    public bool? CanReadAllGroupMessages { get; set; }

    [JsonPropertyName("supports_inline_queries")]
    public bool? SupportsInlineQueries { get; set; }
}
