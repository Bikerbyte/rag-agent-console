using RagAgentConsole.Models;

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

public interface IOpenAiCredentialValidator
{
    Task<OpenAiCredentialValidationResult> ValidateAsync(
        OpenAiCredentialValidationRequest request,
        CancellationToken cancellationToken = default);
}

public sealed record AppSettingUpdate(string Key, string? Value, bool IsSecret = false);

public sealed record OpenAiCredentialValidationRequest(
    string ApiBaseUrl,
    string ApiKey,
    string ChatModel,
    string EmbeddingModel);

public sealed record OpenAiCredentialValidationResult(
    OpenAiCredentialValidationStatus Status,
    string Message,
    IReadOnlyList<string> UnavailableModels)
{
    public bool IsValid => Status == OpenAiCredentialValidationStatus.Valid;
}

public enum OpenAiCredentialValidationStatus
{
    Valid,
    InvalidApiKey,
    Forbidden,
    RateLimited,
    ServiceUnavailable,
    InvalidResponse,
    ModelUnavailable,
    Rejected
}
