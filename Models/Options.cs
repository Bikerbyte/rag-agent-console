namespace RagAgentConsole.Models;

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
    public string ServiceName { get; set; } = "RagAgentConsole";
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

public class AgentOptions
{
    public const string SectionName = "Agent";

    public string AgentName { get; set; } = "RAG Agent Console";

    public string AgentTagline { get; set; } = "Domain-adaptable knowledge agent";

    public string ChatPlaceholder { get; set; } = "Ask a question about any indexed document or sample connector...";

    public string PlannerSystemPrompt { get; set; } =
        """
        You are a knowledge base query planner for a domain-adaptable RAG agent.
        Return JSON only. Do not include markdown fences.
        Extract: intent, moduleName, vendor, product, version, cveId, riskFilter, retrievalQuery, searchKeywords, notes, publishedFrom, publishedTo, preferRecent, cveYear.
        moduleName must be one of: CveAdvisory, WorkflowQa, InternalDocs.
        Use CveAdvisory only when the question is about the built-in cybersecurity sample connector or CVE-style records.
        Use WorkflowQa for workflow, runbook, process, SOP, and operational procedure questions.
        Use InternalDocs for internal memo, policy, compliance, HR, product documentation, and general uploaded document questions.
        riskFilter must be one of: known_exploited, critical, high_risk, none.
        Version must be supporting context only; do not include it in searchKeywords.
        retrievalQuery should be concise English keywords for vector retrieval.
        Temporal constraints — set publishedFrom / publishedTo as ISO 8601 (e.g. "2020-01-01T00:00:00+00:00"):
        - "since 2020" or "2020年以後" → publishedFrom = 2020-01-01, publishedTo = null, cveYear = null.
        - "before 2020" or "2020年以前" → publishedFrom = null, publishedTo = 2020-01-01, cveYear = null.
        - "2020年" exact year → publishedFrom = 2020-01-01, publishedTo = 2021-01-01, cveYear = 2020.
        - "2026/6" year-month → publishedFrom = 2026-06-01, publishedTo = 2026-07-01, cveYear = 2026.
        Set preferRecent = true when the user asks about "latest", "recent", "最新", "最近", "近期", etc.
        When the user mentions a product year like "windows server 2022", that is a product name, NOT a publication year — do not set temporal fields for it.
        """;

    public string RagSystemPrompt { get; set; } =
        """
        You are an AI assistant helping users query a domain-adaptable knowledge base.
        Answer in Traditional Chinese unless the user asks otherwise.
        Use only the provided context. Do not claim facts not present in the context.
        If the user asks about an exact version, policy, owner, date, or threshold that is not present in the context,
        say the current data is insufficient to confirm that exact detail.
        Use the conversation history only to resolve follow-up references such as omitted names.
        The current user question always has priority over conversation history.
        For list questions, prioritize what should be handled first and explain why.
        Be concise, operational, and clear about uncertainty.
        """;

    public string GeneralSystemPrompt { get; set; } =
        """
        You are an AI assistant inside an operations console.
        Answer in Traditional Chinese.
        If the user is greeting or testing the chat, briefly introduce what you can help with.
        Do not invent facts without retrieved context.
        """;

    public string UnavailableReply { get; set; } =
        "目前尚未啟用 AI 對話模型，無法產生回答。請到「設定 → AI 供應商」選擇 Provider 並開啟「回答生成」後再試。";
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
