using RagAgentConsole.Models;
using RagAgentConsole.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace RagAgentConsole.Pages.Settings;

public class IndexModel(
    IAppSettingsService appSettingsService,
    IOpenAiCredentialValidator openAiCredentialValidator) : PageModel
{
    public const string SecretMask = "************";

    public bool HasOpenAiApiKey { get; private set; }
    public bool BotEnabled { get; private set; }
    public bool HasTelegramBotToken { get; private set; }
    public bool HasWebhookSecretToken { get; private set; }
    public string AiStatusLabel { get; private set; } = "Offline";
    public string AiStatusDetail { get; private set; } = string.Empty;
    public string AiStatusClass { get; private set; } = "is-warning";
    public string VectorStoreStatusLabel { get; private set; } = "EfJson";
    public string VectorStoreStatusClass { get; private set; } = "is-neutral";
    public IReadOnlyList<SettingsHealthItem> AiHealthItems { get; private set; } = [];

    [BindProperty]
    public AppSettingsInput Input { get; set; } = new();

    [TempData]
    public string? StatusMessage { get; set; }

    public AgentOptions DefaultAgentOptions { get; } = new();

    public async Task OnGetAsync(CancellationToken cancellationToken)
    {
        await LoadAsync(cancellationToken);
    }

    public async Task<IActionResult> OnPostSaveAsync(CancellationToken cancellationToken)
    {
        var currentAi = await appSettingsService.GetAiProviderOptionsAsync(cancellationToken);
        var currentTelegram = await appSettingsService.GetTelegramBotOptionsAsync(cancellationToken);

        ValidateUrl(Input.OpenAiApiBaseUrl, nameof(Input.OpenAiApiBaseUrl), "OpenAI Base URL must start with http:// or https://.");
        ValidateUrl(Input.OllamaApiBaseUrl, nameof(Input.OllamaApiBaseUrl), "Ollama Base URL must start with http:// or https://.");
        ValidateUrl(Input.TelegramApiBaseUrl, nameof(Input.TelegramApiBaseUrl), "Telegram API base URL must start with http:// or https://.");
        if (!ModelState.IsValid)
        {
            return await ReturnPageWithoutSecretsAsync(Input, cancellationToken);
        }

        var submittedOpenAiApiKey = Input.OpenAiApiKey?.Trim();
        var validatedNewOpenAiKey = false;
        if (!string.IsNullOrWhiteSpace(submittedOpenAiApiKey))
        {
            var validation = await openAiCredentialValidator.ValidateAsync(
                new OpenAiCredentialValidationRequest(
                    Input.OpenAiApiBaseUrl,
                    submittedOpenAiApiKey,
                    Input.OpenAiChatModel,
                    Input.OpenAiEmbeddingModel),
                cancellationToken);

            if (!validation.IsValid)
            {
                return await ReturnPageWithoutSecretsAsync(Input, cancellationToken, validation);
            }

            validatedNewOpenAiKey = true;
        }

        var updates = new List<AppSettingUpdate>
        {
            new("AiProvider:Provider", Input.AiProvider),
            new("AiProvider:EnableChatGeneration", Input.EnableChatGeneration.ToString()),
            new("AiProvider:UseLocalFallback", Input.UseLocalFallback.ToString()),
            new("AiProvider:OpenAiApiBaseUrl", Input.OpenAiApiBaseUrl),
            new("AiProvider:OpenAiChatModel", Input.OpenAiChatModel),
            new("AiProvider:OpenAiEmbeddingModel", Input.OpenAiEmbeddingModel),
            new("AiProvider:OllamaApiBaseUrl", Input.OllamaApiBaseUrl),
            new("AiProvider:OllamaChatModel", Input.OllamaChatModel),
            new("AiProvider:OllamaEmbeddingModel", Input.OllamaEmbeddingModel),
            new("TelegramBot:Enabled", Input.TelegramEnabled.ToString()),
            new("TelegramBot:ApiBaseUrl", Input.TelegramApiBaseUrl),
            new("TelegramBot:UseWebhookMode", Input.UseWebhookMode.ToString()),
            new("TelegramBot:WebhookUrl", Input.WebhookUrl),
            new("TelegramBot:WebhookPath", Input.WebhookPath),
            new("TelegramBot:PollingDelaySeconds", Input.PollingDelaySeconds.ToString()),
            new("DataSources:AutoSyncIntervalMinutes", Input.AutoSyncIntervalMinutes.ToString()),
            new("PushNotifications:Enabled", Input.PushEnabled.ToString()),
            new("PushNotifications:EnableSecurityAdvisoryPush", Input.SecurityAdvisoryPushEnabled.ToString()),
            new("PushNotifications:WorkerIntervalSeconds", Input.PushWorkerIntervalSeconds.ToString()),
            new("PushNotifications:AdvisoryLookbackHours", Input.AdvisoryLookbackHours.ToString()),
            new("VectorStore:Provider", Input.VectorStoreProvider),
            new("VectorStore:CandidateLimit", Input.VectorStoreCandidateLimit.ToString()),
            new("VectorStore:UseJsonFallback", Input.VectorStoreUseJsonFallback.ToString()),
            new("Observability:EnableOpenTelemetry", Input.EnableOpenTelemetry.ToString()),
            new("Observability:EnableConsoleExporter", Input.EnableOpenTelemetryConsoleExporter.ToString()),
            new("Observability:ServiceName", Input.OpenTelemetryServiceName)
        };

        updates.Add(new AppSettingUpdate(
            "AiProvider:OpenAiApiKey",
            string.IsNullOrWhiteSpace(submittedOpenAiApiKey) ? currentAi.OpenAiApiKey : submittedOpenAiApiKey,
            IsSecret: true));

        updates.Add(new AppSettingUpdate(
            "TelegramBot:BotToken",
            string.IsNullOrWhiteSpace(Input.TelegramBotToken) ? currentTelegram.BotToken : Input.TelegramBotToken,
            IsSecret: true));

        updates.Add(new AppSettingUpdate(
            "TelegramBot:WebhookSecretToken",
            string.IsNullOrWhiteSpace(Input.WebhookSecretToken) ? currentTelegram.WebhookSecretToken : Input.WebhookSecretToken,
            IsSecret: true));

        updates.Add(new AppSettingUpdate("Agent:AgentName", Input.AgentName));
        updates.Add(new AppSettingUpdate("Agent:AgentTagline", Input.AgentTagline));
        updates.Add(new AppSettingUpdate("Agent:ChatPlaceholder", Input.ChatPlaceholder));
        updates.Add(new AppSettingUpdate("Agent:PlannerSystemPrompt", Input.PlannerSystemPrompt));
        updates.Add(new AppSettingUpdate("Agent:RagSystemPrompt", Input.RagSystemPrompt));
        updates.Add(new AppSettingUpdate("Agent:GeneralSystemPrompt", Input.GeneralSystemPrompt));
        updates.Add(new AppSettingUpdate("Agent:UnavailableReply", Input.UnavailableReply));

        await appSettingsService.SaveAsync(updates, cancellationToken);
        StatusMessage = validatedNewOpenAiKey
            ? "OpenAI API key 與模型存取驗證成功，設定已儲存。"
            : "設定已儲存。AI、Telegram、Agent 與 RAG retrieval 設定會在後續請求套用；worker role 與 OpenTelemetry 啟動設定需重新啟動。";
        return RedirectToPage();
    }

    private void AddOpenAiValidationError(OpenAiCredentialValidationResult validation)
    {
        var fieldName = validation.Status == OpenAiCredentialValidationStatus.ModelUnavailable
            ? validation.UnavailableModels.Contains(Input.OpenAiEmbeddingModel, StringComparer.OrdinalIgnoreCase)
                ? nameof(Input.OpenAiEmbeddingModel)
                : nameof(Input.OpenAiChatModel)
            : nameof(Input.OpenAiApiKey);

        ModelState.Remove($"Input.{nameof(Input.OpenAiApiKey)}");
        ModelState.AddModelError($"Input.{fieldName}", validation.Message);
    }

    private async Task<IActionResult> ReturnPageWithoutSecretsAsync(
        AppSettingsInput submittedInput,
        CancellationToken cancellationToken,
        OpenAiCredentialValidationResult? openAiValidation = null)
    {
        ModelState.Remove($"Input.{nameof(Input.OpenAiApiKey)}");
        ModelState.Remove($"Input.{nameof(Input.TelegramBotToken)}");
        ModelState.Remove($"Input.{nameof(Input.WebhookSecretToken)}");
        if (openAiValidation is not null)
        {
            AddOpenAiValidationError(openAiValidation);
        }

        submittedInput.OpenAiApiKey = null;
        submittedInput.TelegramBotToken = null;
        submittedInput.WebhookSecretToken = null;

        await LoadAsync(cancellationToken);
        Input = submittedInput;
        return Page();
    }

    private async Task LoadAsync(CancellationToken cancellationToken)
    {
        var ai = await appSettingsService.GetAiProviderOptionsAsync(cancellationToken);
        var telegram = await appSettingsService.GetTelegramBotOptionsAsync(cancellationToken);
        var dataSource = await appSettingsService.GetDataSourceOptionsAsync(cancellationToken);
        var push = await appSettingsService.GetPushNotificationOptionsAsync(cancellationToken);
        var vectorStore = await appSettingsService.GetVectorStoreOptionsAsync(cancellationToken);
        var observability = await appSettingsService.GetObservabilityOptionsAsync(cancellationToken);
        var agent = await appSettingsService.GetAgentOptionsAsync(cancellationToken);

        HasOpenAiApiKey = !string.IsNullOrWhiteSpace(ai.OpenAiApiKey);
        BotEnabled = telegram.Enabled;
        HasTelegramBotToken = !string.IsNullOrWhiteSpace(telegram.BotToken);
        HasWebhookSecretToken = !string.IsNullOrWhiteSpace(telegram.WebhookSecretToken);
        Input = AppSettingsInput.From(ai, telegram, dataSource, push, vectorStore, observability, agent);
        SetAiStatus(ai);
        SetVectorStoreStatus(vectorStore);
    }

    private void SetVectorStoreStatus(VectorStoreOptions options)
    {
        VectorStoreStatusLabel = options.Provider;
        VectorStoreStatusClass = string.Equals(options.Provider, VectorStoreProviderNames.PgVector, StringComparison.OrdinalIgnoreCase)
            ? "is-success"
            : "is-neutral";
    }

    private void SetAiStatus(AiProviderOptions options)
    {
        var isLocal = string.Equals(options.Provider, AiProviderNames.Local, StringComparison.OrdinalIgnoreCase);
        var isOpenAi = string.Equals(options.Provider, AiProviderNames.OpenAI, StringComparison.OrdinalIgnoreCase);
        var isOllama = string.Equals(options.Provider, AiProviderNames.Ollama, StringComparison.OrdinalIgnoreCase);
        var hasOpenAiKey = !string.IsNullOrWhiteSpace(options.OpenAiApiKey);
        var isAiChatEnabled = options.EnableChatGeneration && !isLocal;
        var credentialReady = !isOpenAi || hasOpenAiKey;

        AiHealthItems =
        [
            new("Provider", isLocal ? "is-neutral" : "is-ready", isLocal ? "Local fallback" : options.Provider),
            new("Chat generation", options.EnableChatGeneration ? "is-ready" : isLocal ? "is-neutral" : "is-missing", options.EnableChatGeneration ? "已開啟" : "尚未開啟"),
            new("Credential", credentialReady ? "is-ready" : "is-missing", isOpenAi && !hasOpenAiKey ? "OpenAI API key 尚未儲存" : "可用")
        ];

        if (isAiChatEnabled && credentialReady)
        {
            AiStatusLabel = "Online";
            AiStatusDetail = isOpenAi
                ? $"OpenAI chat model: {options.OpenAiChatModel}"
                : isOllama
                    ? $"Ollama chat model: {options.OllamaChatModel}"
                    : "Chat generation enabled";
            AiStatusClass = "is-success";
            return;
        }

        if (isLocal && hasOpenAiKey)
        {
            AiStatusLabel = "Key saved, not active";
            AiStatusDetail = "OpenAI key 已儲存，但 Provider 仍是 Local。請切到 OpenAI 並開啟 Chat generation。";
            AiStatusClass = "is-warning";
            return;
        }

        if (isOpenAi && !hasOpenAiKey)
        {
            AiStatusLabel = "Needs API key";
            AiStatusDetail = "Provider 已選 OpenAI，但尚未儲存 OpenAI API key。";
            AiStatusClass = "is-warning";
            return;
        }

        if (!options.EnableChatGeneration && !isLocal)
        {
            AiStatusLabel = "Chat disabled";
            AiStatusDetail = "Provider 已選擇，但 Chat generation 尚未開啟。";
            AiStatusClass = "is-warning";
            return;
        }

        AiStatusLabel = "Offline";
        AiStatusDetail = "目前使用 Local fallback，不會呼叫 OpenAI 或 Ollama。";
        AiStatusClass = "is-neutral";
    }

    private void ValidateUrl(string? value, string propertyName, string errorMessage)
    {
        if (!SettingsUrlValidator.IsAbsoluteHttpUrl(value))
        {
            ModelState.AddModelError($"Input.{propertyName}", errorMessage);
        }
    }

    public sealed record SettingsHealthItem(string Label, string StatusClass, string Detail);

    public class AppSettingsInput
    {
        public string AgentName { get; set; } = "AI Assistant";
        public string AgentTagline { get; set; } = new AgentOptions().AgentTagline;
        public string ChatPlaceholder { get; set; } = new AgentOptions().ChatPlaceholder;
        public string PlannerSystemPrompt { get; set; } = new AgentOptions().PlannerSystemPrompt;
        public string RagSystemPrompt { get; set; } = new AgentOptions().RagSystemPrompt;
        public string GeneralSystemPrompt { get; set; } = new AgentOptions().GeneralSystemPrompt;
        public string UnavailableReply { get; set; } = new AgentOptions().UnavailableReply;
        public string AiProvider { get; set; } = AiProviderNames.Local;
        public bool EnableChatGeneration { get; set; }
        public bool UseLocalFallback { get; set; } = true;
        public string OpenAiApiBaseUrl { get; set; } = "https://api.openai.com";
        public string? OpenAiApiKey { get; set; }
        public string OpenAiChatModel { get; set; } = "gpt-4o-mini";
        public string OpenAiEmbeddingModel { get; set; } = "text-embedding-3-small";
        public string OllamaApiBaseUrl { get; set; } = "http://localhost:11434";
        public string OllamaChatModel { get; set; } = "llama3.1";
        public string OllamaEmbeddingModel { get; set; } = "nomic-embed-text";
        public bool TelegramEnabled { get; set; }
        public string? TelegramBotToken { get; set; }
        public string TelegramApiBaseUrl { get; set; } = "https://api.telegram.org";
        public bool UseWebhookMode { get; set; }
        public string? WebhookUrl { get; set; }
        public string WebhookPath { get; set; } = "/api/telegram/webhook";
        public string? WebhookSecretToken { get; set; }
        public int PollingDelaySeconds { get; set; } = 3;
        public int AutoSyncIntervalMinutes { get; set; } = 15;
        public bool PushEnabled { get; set; } = true;
        public bool SecurityAdvisoryPushEnabled { get; set; } = true;
        public int PushWorkerIntervalSeconds { get; set; } = 90;
        public int AdvisoryLookbackHours { get; set; } = 72;
        public string VectorStoreProvider { get; set; } = VectorStoreProviderNames.EfJson;
        public int VectorStoreCandidateLimit { get; set; } = 3000;
        public bool VectorStoreUseJsonFallback { get; set; } = true;
        public bool EnableOpenTelemetry { get; set; }
        public bool EnableOpenTelemetryConsoleExporter { get; set; }
        public string OpenTelemetryServiceName { get; set; } = "RagAgentConsole";

        public static AppSettingsInput From(
            AiProviderOptions ai,
            TelegramBotOptions telegram,
            DataSourceOptions dataSource,
            PushNotificationOptions push,
            VectorStoreOptions vectorStore,
            ObservabilityOptions observability,
            AgentOptions agent)
            => new()
            {
                AgentName = agent.AgentName,
                AgentTagline = agent.AgentTagline,
                ChatPlaceholder = agent.ChatPlaceholder,
                PlannerSystemPrompt = agent.PlannerSystemPrompt,
                RagSystemPrompt = agent.RagSystemPrompt,
                GeneralSystemPrompt = agent.GeneralSystemPrompt,
                UnavailableReply = agent.UnavailableReply,
                AiProvider = ai.Provider,
                EnableChatGeneration = ai.EnableChatGeneration,
                UseLocalFallback = ai.UseLocalFallback,
                OpenAiApiBaseUrl = ai.OpenAiApiBaseUrl,
                OpenAiChatModel = ai.OpenAiChatModel,
                OpenAiEmbeddingModel = ai.OpenAiEmbeddingModel,
                OllamaApiBaseUrl = ai.OllamaApiBaseUrl,
                OllamaChatModel = ai.OllamaChatModel,
                OllamaEmbeddingModel = ai.OllamaEmbeddingModel,
                TelegramEnabled = telegram.Enabled,
                TelegramApiBaseUrl = telegram.ApiBaseUrl,
                UseWebhookMode = telegram.UseWebhookMode,
                WebhookUrl = telegram.WebhookUrl,
                WebhookPath = telegram.WebhookPath,
                PollingDelaySeconds = telegram.PollingDelaySeconds,
                AutoSyncIntervalMinutes = dataSource.AutoSyncIntervalMinutes,
                PushEnabled = push.Enabled,
                SecurityAdvisoryPushEnabled = push.EnableSecurityAdvisoryPush,
                PushWorkerIntervalSeconds = push.WorkerIntervalSeconds,
                AdvisoryLookbackHours = push.AdvisoryLookbackHours,
                VectorStoreProvider = vectorStore.Provider,
                VectorStoreCandidateLimit = vectorStore.CandidateLimit,
                VectorStoreUseJsonFallback = vectorStore.UseJsonFallback,
                EnableOpenTelemetry = observability.EnableOpenTelemetry,
                EnableOpenTelemetryConsoleExporter = observability.EnableConsoleExporter,
                OpenTelemetryServiceName = observability.ServiceName
            };
    }
}
