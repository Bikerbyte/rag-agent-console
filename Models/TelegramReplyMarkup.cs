using System.Text.Json.Serialization;

namespace CPBLLineBotCloud.Models;

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
