using System.Text.Json.Serialization;

namespace CPBLLineBotCloud.Models;

public class TelegramSendMessageRequest
{
    [JsonPropertyName("chat_id")]
    public required string ChatId { get; set; }

    [JsonPropertyName("text")]
    public required string Text { get; set; }

    [JsonPropertyName("reply_markup")]
    public object? ReplyMarkup { get; set; }
}
