namespace SecurityAdvisoryBot.Models;

public class AppRuntimeOptions
{
    public const string SectionName = "AppRuntime";

    public string Profile { get; set; } = AppRuntimeProfiles.Standard;
    public string InstanceName { get; set; } = "local-node";
    public bool EnableLeadershipLease { get; set; } = true;
    public int LeaseDurationSeconds { get; set; } = 45;
    public int LeaseRenewIntervalSeconds { get; set; } = 15;
    public int LeaseAcquireRetrySeconds { get; set; } = 10;
    public bool EnableTelegramWebhookIngress { get; set; } = true;
    public bool EnableTelegramPollingWorker { get; set; } = true;
    public bool EnableTelegramUpdateQueueWorker { get; set; } = true;
    public bool EnableOfficialDataSyncWorker { get; set; } = true;
    public bool EnableNotificationWorker { get; set; } = true;
}

public static class AppRuntimeProfiles
{
    public const string Standard = "Standard";
    public const string WorkerOnly = "WorkerOnly";
    public const string IngressOnly = "IngressOnly";
    public const string PollingNode = "PollingNode";
    public const string Custom = "Custom";

    public static string Normalize(string? profile)
    {
        if (string.IsNullOrWhiteSpace(profile))
        {
            return Standard;
        }

        return profile.Trim() switch
        {
            var value when value.Equals(Standard, StringComparison.OrdinalIgnoreCase) => Standard,
            var value when value.Equals(WorkerOnly, StringComparison.OrdinalIgnoreCase) => WorkerOnly,
            var value when value.Equals(IngressOnly, StringComparison.OrdinalIgnoreCase) => IngressOnly,
            var value when value.Equals(PollingNode, StringComparison.OrdinalIgnoreCase) => PollingNode,
            var value when value.Equals(Custom, StringComparison.OrdinalIgnoreCase) => Custom,
            _ => Custom
        };
    }
}

public class DataSourceOptions
{
    public const string SectionName = "DataSources";

    public int AutoSyncIntervalMinutes { get; set; } = 15;
}

public class PushNotificationOptions
{
    public const string SectionName = "PushNotifications";

    public bool Enabled { get; set; } = true;
    public bool EnableSecurityAdvisoryPush { get; set; } = true;
    public int WorkerIntervalSeconds { get; set; } = 90;
    public int AdvisoryLookbackHours { get; set; } = 72;
}

public class SecurityAdvisoryOptions
{
    public const string SectionName = "SecurityAdvisories";

    public bool EnableCisaKevSource { get; set; } = true;
    public bool EnableNvdSource { get; set; } = true;
    public string CisaKevJsonUrl { get; set; } = "https://www.cisa.gov/sites/default/files/feeds/known_exploited_vulnerabilities.json";
    public string NvdApiBaseUrl { get; set; } = "https://services.nvd.nist.gov";
    public int NvdLookbackDays { get; set; } = 7;
    public int MaxNvdResultsPerSync { get; set; } = 100;
    public int EmbeddingDimensions { get; set; } = 384;
    public int RagMaxChunks { get; set; } = 5;
}

public class VectorStoreOptions
{
    public const string SectionName = "VectorStore";

    public string Provider { get; set; } = VectorStoreProviderNames.EfJson;
    public int CandidateLimit { get; set; } = 3000;
    public bool UseJsonFallback { get; set; } = true;
}

public static class VectorStoreProviderNames
{
    public const string EfJson = "EfJson";
    public const string PgVector = "PgVector";
}

public class ObservabilityOptions
{
    public const string SectionName = "Observability";

    public bool EnableOpenTelemetry { get; set; }
    public bool EnableConsoleExporter { get; set; }
    public string ServiceName { get; set; } = "SecurityAdvisoryBot";
}

public class TelegramBotOptions
{
    public const string SectionName = "TelegramBot";

    public bool Enabled { get; set; }
    public string BotToken { get; set; } = string.Empty;
    public string ApiBaseUrl { get; set; } = "https://api.telegram.org";
    public int PollingDelaySeconds { get; set; } = 3;
    public bool UseWebhookMode { get; set; }
    public string WebhookPath { get; set; } = "/api/telegram/webhook";
    public string WebhookUrl { get; set; } = string.Empty;
    public string WebhookSecretToken { get; set; } = string.Empty;
}

public class AiProviderOptions
{
    public const string SectionName = "AiProvider";

    public string Provider { get; set; } = AiProviderNames.Local;
    public bool EnableChatGeneration { get; set; }
    public bool UseLocalFallback { get; set; } = true;
    public string OpenAiApiBaseUrl { get; set; } = "https://api.openai.com";
    public string OpenAiApiKey { get; set; } = string.Empty;
    public string OpenAiChatModel { get; set; } = "gpt-4o-mini";
    public string OpenAiEmbeddingModel { get; set; } = "text-embedding-3-small";
    public string OllamaApiBaseUrl { get; set; } = "http://localhost:11434";
    public string OllamaChatModel { get; set; } = "llama3.1";
    public string OllamaEmbeddingModel { get; set; } = "nomic-embed-text";
    public int ChatTimeoutSeconds { get; set; } = 45;
    public int EmbeddingTimeoutSeconds { get; set; } = 45;
}

public static class AiProviderNames
{
    public const string Local = "Local";
    public const string OpenAI = "OpenAI";
    public const string Ollama = "Ollama";
}
