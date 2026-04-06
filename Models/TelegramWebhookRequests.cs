using System.Text.Json.Serialization;

namespace CPBLLineBotCloud.Models;

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
