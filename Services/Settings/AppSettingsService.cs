using RagAgentConsole.Data;
using RagAgentConsole.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace RagAgentConsole.Services;

public interface IAppSettingsService
{
    Task<AiProviderOptions> GetAiProviderOptionsAsync(CancellationToken cancellationToken = default);
    Task<TelegramBotOptions> GetTelegramBotOptionsAsync(CancellationToken cancellationToken = default);
    Task<DataSourceOptions> GetDataSourceOptionsAsync(CancellationToken cancellationToken = default);
    Task<PushNotificationOptions> GetPushNotificationOptionsAsync(CancellationToken cancellationToken = default);
    Task<VectorStoreOptions> GetVectorStoreOptionsAsync(CancellationToken cancellationToken = default);
    Task<ObservabilityOptions> GetObservabilityOptionsAsync(CancellationToken cancellationToken = default);
    Task<AgentOptions> GetAgentOptionsAsync(CancellationToken cancellationToken = default);
    Task<IReadOnlyDictionary<string, AppSetting>> GetAllAsync(CancellationToken cancellationToken = default);
    Task SaveAsync(IEnumerable<AppSettingUpdate> updates, CancellationToken cancellationToken = default);
}

public sealed record AppSettingUpdate(string Key, string? Value, bool IsSecret = false);

public class AppSettingsService(
    ApplicationDbContext dbContext,
    IOptions<AiProviderOptions> aiOptions,
    IOptions<TelegramBotOptions> telegramOptions,
    IOptions<DataSourceOptions> dataSourceOptions,
    IOptions<PushNotificationOptions> pushOptions,
    IOptions<VectorStoreOptions> vectorStoreOptions,
    IOptions<ObservabilityOptions> observabilityOptions,
    IOptions<AgentOptions> agentOptions) : IAppSettingsService
{
    public async Task<IReadOnlyDictionary<string, AppSetting>> GetAllAsync(CancellationToken cancellationToken = default)
        => await dbContext.AppSettings
            .AsNoTracking()
            .ToDictionaryAsync(setting => setting.SettingKey, StringComparer.OrdinalIgnoreCase, cancellationToken);

    public async Task<AiProviderOptions> GetAiProviderOptionsAsync(CancellationToken cancellationToken = default)
    {
        var values = await GetAllAsync(cancellationToken);
        var options = Clone(aiOptions.Value);

        options.Provider = Get(values, "AiProvider:Provider", options.Provider);
        options.EnableChatGeneration = GetBool(values, "AiProvider:EnableChatGeneration", options.EnableChatGeneration);
        options.UseLocalFallback = GetBool(values, "AiProvider:UseLocalFallback", options.UseLocalFallback);
        options.OpenAiApiBaseUrl = SettingsUrlValidator.UseFallbackUnlessAbsoluteHttpUrl(
            Get(values, "AiProvider:OpenAiApiBaseUrl", options.OpenAiApiBaseUrl),
            SettingsUrlValidator.DefaultOpenAiApiBaseUrl);
        options.OpenAiApiKey = Get(values, "AiProvider:OpenAiApiKey", options.OpenAiApiKey);
        options.OpenAiChatModel = Get(values, "AiProvider:OpenAiChatModel", options.OpenAiChatModel);
        options.OpenAiEmbeddingModel = Get(values, "AiProvider:OpenAiEmbeddingModel", options.OpenAiEmbeddingModel);
        options.OllamaApiBaseUrl = SettingsUrlValidator.UseFallbackUnlessAbsoluteHttpUrl(
            Get(values, "AiProvider:OllamaApiBaseUrl", options.OllamaApiBaseUrl),
            SettingsUrlValidator.DefaultOllamaApiBaseUrl);
        options.OllamaChatModel = Get(values, "AiProvider:OllamaChatModel", options.OllamaChatModel);
        options.OllamaEmbeddingModel = Get(values, "AiProvider:OllamaEmbeddingModel", options.OllamaEmbeddingModel);
        options.ChatTimeoutSeconds = GetInt(values, "AiProvider:ChatTimeoutSeconds", options.ChatTimeoutSeconds);
        options.EmbeddingTimeoutSeconds = GetInt(values, "AiProvider:EmbeddingTimeoutSeconds", options.EmbeddingTimeoutSeconds);
        return options;
    }

    public async Task<TelegramBotOptions> GetTelegramBotOptionsAsync(CancellationToken cancellationToken = default)
    {
        var values = await GetAllAsync(cancellationToken);
        var options = Clone(telegramOptions.Value);

        options.Enabled = GetBool(values, "TelegramBot:Enabled", options.Enabled);
        options.BotToken = Get(values, "TelegramBot:BotToken", options.BotToken);
        options.ApiBaseUrl = SettingsUrlValidator.UseFallbackUnlessAbsoluteHttpUrl(
            Get(values, "TelegramBot:ApiBaseUrl", options.ApiBaseUrl),
            SettingsUrlValidator.DefaultTelegramApiBaseUrl);
        options.PollingDelaySeconds = GetInt(values, "TelegramBot:PollingDelaySeconds", options.PollingDelaySeconds);
        options.UseWebhookMode = GetBool(values, "TelegramBot:UseWebhookMode", options.UseWebhookMode);
        options.WebhookPath = Get(values, "TelegramBot:WebhookPath", options.WebhookPath);
        options.WebhookUrl = Get(values, "TelegramBot:WebhookUrl", options.WebhookUrl);
        options.WebhookSecretToken = Get(values, "TelegramBot:WebhookSecretToken", options.WebhookSecretToken);
        return options;
    }

    public async Task<DataSourceOptions> GetDataSourceOptionsAsync(CancellationToken cancellationToken = default)
    {
        var values = await GetAllAsync(cancellationToken);
        var options = Clone(dataSourceOptions.Value);
        options.AutoSyncIntervalMinutes = GetInt(values, "DataSources:AutoSyncIntervalMinutes", options.AutoSyncIntervalMinutes);
        return options;
    }

    public async Task<PushNotificationOptions> GetPushNotificationOptionsAsync(CancellationToken cancellationToken = default)
    {
        var values = await GetAllAsync(cancellationToken);
        var options = Clone(pushOptions.Value);
        options.Enabled = GetBool(values, "PushNotifications:Enabled", options.Enabled);
        options.EnableSecurityAdvisoryPush = GetBool(values, "PushNotifications:EnableSecurityAdvisoryPush", options.EnableSecurityAdvisoryPush);
        options.WorkerIntervalSeconds = GetInt(values, "PushNotifications:WorkerIntervalSeconds", options.WorkerIntervalSeconds);
        options.AdvisoryLookbackHours = GetInt(values, "PushNotifications:AdvisoryLookbackHours", options.AdvisoryLookbackHours);
        return options;
    }

    public async Task<VectorStoreOptions> GetVectorStoreOptionsAsync(CancellationToken cancellationToken = default)
    {
        var values = await GetAllAsync(cancellationToken);
        var options = Clone(vectorStoreOptions.Value);
        options.CandidateLimit = GetInt(values, "VectorStore:CandidateLimit", options.CandidateLimit);
        return options;
    }

    public async Task<ObservabilityOptions> GetObservabilityOptionsAsync(CancellationToken cancellationToken = default)
    {
        var values = await GetAllAsync(cancellationToken);
        var options = Clone(observabilityOptions.Value);
        options.EnableOpenTelemetry = GetBool(values, "Observability:EnableOpenTelemetry", options.EnableOpenTelemetry);
        options.EnableConsoleExporter = GetBool(values, "Observability:EnableConsoleExporter", options.EnableConsoleExporter);
        options.ServiceName = Get(values, "Observability:ServiceName", options.ServiceName);
        return options;
    }

    public async Task<AgentOptions> GetAgentOptionsAsync(CancellationToken cancellationToken = default)
    {
        var values = await GetAllAsync(cancellationToken);
        var options = Clone(agentOptions.Value);
        options.AgentName = Get(values, "Agent:AgentName", options.AgentName);
        options.AgentTagline = Get(values, "Agent:AgentTagline", options.AgentTagline);
        options.ChatPlaceholder = Get(values, "Agent:ChatPlaceholder", options.ChatPlaceholder);
        options.DefaultDomain = Get(values, "Agent:DefaultDomain", options.DefaultDomain);
        options.PlannerSystemPrompt = Get(values, "Agent:PlannerSystemPrompt", options.PlannerSystemPrompt);
        options.RagSystemPrompt = Get(values, "Agent:RagSystemPrompt", options.RagSystemPrompt);
        options.GeneralSystemPrompt = Get(values, "Agent:GeneralSystemPrompt", options.GeneralSystemPrompt);
        options.UnavailableReply = Get(values, "Agent:UnavailableReply", options.UnavailableReply);
        return options;
    }

    public async Task SaveAsync(IEnumerable<AppSettingUpdate> updates, CancellationToken cancellationToken = default)
    {
        var now = DateTimeOffset.UtcNow;
        foreach (var update in updates)
        {
            var key = update.Key.Trim();
            var setting = await dbContext.AppSettings.FirstOrDefaultAsync(item => item.SettingKey == key, cancellationToken);
            if (setting is null)
            {
                setting = new AppSetting
                {
                    SettingKey = key,
                    UpdatedTime = now
                };
                dbContext.AppSettings.Add(setting);
            }

            setting.SettingValue = string.IsNullOrWhiteSpace(update.Value) ? null : update.Value.Trim();
            setting.IsSecret = update.IsSecret;
            setting.UpdatedTime = now;
        }

        await dbContext.SaveChangesAsync(cancellationToken);
    }

    private static string Get(IReadOnlyDictionary<string, AppSetting> values, string key, string fallback)
        => values.TryGetValue(key, out var setting) && !string.IsNullOrWhiteSpace(setting.SettingValue)
            ? setting.SettingValue
            : fallback;

    private static bool GetBool(IReadOnlyDictionary<string, AppSetting> values, string key, bool fallback)
        => values.TryGetValue(key, out var setting) && bool.TryParse(setting.SettingValue, out var value) ? value : fallback;

    private static int GetInt(IReadOnlyDictionary<string, AppSetting> values, string key, int fallback)
        => values.TryGetValue(key, out var setting) && int.TryParse(setting.SettingValue, out var value) ? value : fallback;

    private static AiProviderOptions Clone(AiProviderOptions source)
        => new()
        {
            Provider = source.Provider,
            EnableChatGeneration = source.EnableChatGeneration,
            UseLocalFallback = source.UseLocalFallback,
            OpenAiApiBaseUrl = source.OpenAiApiBaseUrl,
            OpenAiApiKey = source.OpenAiApiKey,
            OpenAiChatModel = source.OpenAiChatModel,
            OpenAiEmbeddingModel = source.OpenAiEmbeddingModel,
            OllamaApiBaseUrl = source.OllamaApiBaseUrl,
            OllamaChatModel = source.OllamaChatModel,
            OllamaEmbeddingModel = source.OllamaEmbeddingModel,
            ChatTimeoutSeconds = source.ChatTimeoutSeconds,
            EmbeddingTimeoutSeconds = source.EmbeddingTimeoutSeconds
        };

    private static TelegramBotOptions Clone(TelegramBotOptions source)
        => new()
        {
            Enabled = source.Enabled,
            BotToken = source.BotToken,
            ApiBaseUrl = source.ApiBaseUrl,
            PollingDelaySeconds = source.PollingDelaySeconds,
            UseWebhookMode = source.UseWebhookMode,
            WebhookPath = source.WebhookPath,
            WebhookUrl = source.WebhookUrl,
            WebhookSecretToken = source.WebhookSecretToken
        };

    private static DataSourceOptions Clone(DataSourceOptions source)
        => new() { AutoSyncIntervalMinutes = source.AutoSyncIntervalMinutes };

    private static PushNotificationOptions Clone(PushNotificationOptions source)
        => new()
        {
            Enabled = source.Enabled,
            EnableSecurityAdvisoryPush = source.EnableSecurityAdvisoryPush,
            WorkerIntervalSeconds = source.WorkerIntervalSeconds,
            AdvisoryLookbackHours = source.AdvisoryLookbackHours
        };

    private static VectorStoreOptions Clone(VectorStoreOptions source)
        => new()
        {
            CandidateLimit = source.CandidateLimit
        };

    private static ObservabilityOptions Clone(ObservabilityOptions source)
        => new()
        {
            EnableOpenTelemetry = source.EnableOpenTelemetry,
            EnableConsoleExporter = source.EnableConsoleExporter,
            OtlpEndpoint = source.OtlpEndpoint,
            ServiceName = source.ServiceName
        };

    private static AgentOptions Clone(AgentOptions source)
        => new()
        {
            AgentName = source.AgentName,
            AgentTagline = source.AgentTagline,
            ChatPlaceholder = source.ChatPlaceholder,
            DefaultDomain = source.DefaultDomain,
            PlannerSystemPrompt = source.PlannerSystemPrompt,
            RagSystemPrompt = source.RagSystemPrompt,
            GeneralSystemPrompt = source.GeneralSystemPrompt,
            UnavailableReply = source.UnavailableReply
        };
}
