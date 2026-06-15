namespace RagAgentConsole.Models;

/// <summary>
/// 角色開關：同一個 image 用環境變數切成 web（只開 ingress）或 worker（只開背景工作）。
/// 多節點的單實例保證交給部署層（k8s 單副本 Deployment / CronJob），app 內不再自己協調。
/// </summary>
public class AppRuntimeOptions
{
    public const string SectionName = "AppRuntime";

    public string InstanceName { get; set; } = string.Empty;
    public bool EnableTelegramWebhookIngress { get; set; } = true;
    public bool EnableTelegramPollingWorker { get; set; } = true;
    public bool EnableTelegramUpdateQueueWorker { get; set; } = true;

    public string GetEffectiveInstanceName()
    {
        if (!string.IsNullOrWhiteSpace(InstanceName))
        {
            return InstanceName.Trim();
        }

        var machineName = Environment.MachineName?.Trim();
        return string.IsNullOrWhiteSpace(machineName) ? $"node-{Environment.ProcessId}" : machineName;
    }
}

public class RagOptions
{
    public const string SectionName = "Rag";

    public int LocalEmbeddingDimensions { get; set; } = 384;
    public int MaxChunks { get; set; } = 5;
}

public class VectorStoreOptions
{
    public const string SectionName = "VectorStore";

    public int CandidateLimit { get; set; } = 3000;
}

public class ObservabilityOptions
{
    public const string SectionName = "Observability";

    public bool EnableOpenTelemetry { get; set; }
    public bool EnableConsoleExporter { get; set; }

    /// <summary>OTLP gRPC endpoint（例如 http://localhost:4317 的 otel-lgtm）。留空就不外送。</summary>
    public string OtlpEndpoint { get; set; } = string.Empty;

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

    public string AgentTagline { get; set; } = "Document knowledge agent";

    public string ChatPlaceholder { get; set; } = "Ask a question about any indexed document...";

    public string DefaultModule { get; set; } = KnowledgeModuleNames.InternalDocs;

    public string PlannerSystemPrompt { get; set; } =
        """
        You are a knowledge base query planner for a document RAG agent.
        Return JSON only. Do not include markdown fences.
        Output fields: intent, moduleName, retrievalQuery, searchKeywords, entities, filters, notes.
        entities is an object of extracted named values; filters is an object of hard document metadata constraints. Use null or omit keys that do not apply.
        moduleName must be one of: WorkflowQa, InternalDocs.
        Use WorkflowQa for workflow, runbook, process, SOP, and operational procedure questions.
        Use InternalDocs for internal memo, policy, compliance, HR, product documentation, and general uploaded document questions.
        Do not invent a hard filter unless the user explicitly asks for that constraint.
        retrievalQuery should be concise English keywords for vector retrieval.
        """;

    public string RagSystemPrompt { get; set; } =
        """
        You are an AI assistant helping users query a document knowledge base.
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
