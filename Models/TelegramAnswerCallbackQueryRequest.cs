using System.Text.Json.Serialization;

namespace SecurityAdvisoryBot.Models;

public class TelegramAnswerCallbackQueryRequest
{
    [JsonPropertyName("callback_query_id")]
    public required string CallbackQueryId { get; set; }
}
