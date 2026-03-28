using System.Text.Json.Serialization;

namespace CPBLLineBotCloud.Models;

public class TelegramAnswerCallbackQueryRequest
{
    [JsonPropertyName("callback_query_id")]
    public required string CallbackQueryId { get; set; }
}
