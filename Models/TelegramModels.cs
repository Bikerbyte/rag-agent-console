using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;

namespace RagAgentConsole.Models;

public class TelegramChatSubscription
{
    public int TelegramChatSubscriptionId { get; set; }

    [MaxLength(64)]
    public required string ChatId { get; set; }

    [MaxLength(128)]
    public required string ChatTitle { get; set; }

    public DateTimeOffset CreatedTime { get; set; }
    public DateTimeOffset LastUpdatedTime { get; set; }
}

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

public class TelegramUpdateResponse
{
    [JsonPropertyName("ok")]
    public bool Ok { get; set; }

    [JsonPropertyName("result")]
    public List<TelegramUpdate> Result { get; set; } = [];
}

public class TelegramUpdate
{
    [JsonPropertyName("update_id")]
    public long UpdateId { get; set; }

    [JsonPropertyName("message")]
    public TelegramMessage? Message { get; set; }

    [JsonPropertyName("callback_query")]
    public TelegramCallbackQuery? CallbackQuery { get; set; }
}

public class TelegramMessage
{
    [JsonPropertyName("message_id")]
    public long MessageId { get; set; }

    [JsonPropertyName("text")]
    public string? Text { get; set; }

    [JsonPropertyName("chat")]
    public TelegramChat? Chat { get; set; }
}

public class TelegramChat
{
    [JsonPropertyName("id")]
    public long Id { get; set; }

    [JsonPropertyName("type")]
    public string? Type { get; set; }

    [JsonPropertyName("title")]
    public string? Title { get; set; }

    [JsonPropertyName("username")]
    public string? Username { get; set; }

    [JsonPropertyName("first_name")]
    public string? FirstName { get; set; }

    [JsonPropertyName("last_name")]
    public string? LastName { get; set; }
}

public class TelegramCallbackQuery
{
    [JsonPropertyName("id")]
    public required string Id { get; set; }

    [JsonPropertyName("data")]
    public string? Data { get; set; }

    [JsonPropertyName("message")]
    public TelegramMessage? Message { get; set; }
}

public class TelegramSendMessageRequest
{
    [JsonPropertyName("chat_id")]
    public required string ChatId { get; set; }

    [JsonPropertyName("text")]
    public required string Text { get; set; }

    [JsonPropertyName("reply_markup")]
    public object? ReplyMarkup { get; set; }
}

public class TelegramAnswerCallbackQueryRequest
{
    [JsonPropertyName("callback_query_id")]
    public required string CallbackQueryId { get; set; }
}

public class TelegramSetWebhookRequest
{
    [JsonPropertyName("url")]
    public required string Url { get; set; }

    [JsonPropertyName("secret_token")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? SecretToken { get; set; }

    [JsonPropertyName("drop_pending_updates")]
    public bool DropPendingUpdates { get; set; }
}

public class TelegramReplyMarkup
{
    [JsonPropertyName("keyboard")]
    public required IReadOnlyList<IReadOnlyList<TelegramKeyboardButton>> Keyboard { get; set; }

    [JsonPropertyName("resize_keyboard")]
    public bool ResizeKeyboard { get; set; } = true;

    [JsonPropertyName("is_persistent")]
    public bool IsPersistent { get; set; } = true;

    [JsonPropertyName("input_field_placeholder")]
    public string? InputFieldPlaceholder { get; set; }
}

public class TelegramKeyboardButton
{
    [JsonPropertyName("text")]
    public required string Text { get; set; }
}

public class TelegramInlineKeyboardMarkup
{
    [JsonPropertyName("inline_keyboard")]
    public required IReadOnlyList<IReadOnlyList<TelegramInlineKeyboardButton>> InlineKeyboard { get; set; }
}

public class TelegramInlineKeyboardButton
{
    [JsonPropertyName("text")]
    public required string Text { get; set; }

    [JsonPropertyName("callback_data")]
    public required string CallbackData { get; set; }
}

public class TelegramSendResult
{
    public bool IsSuccess { get; init; }
    public string? ErrorMessage { get; init; }
}
