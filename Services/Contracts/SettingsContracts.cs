using SecurityAdvisoryBot.Models;

namespace SecurityAdvisoryBot.Services;

public interface IAppSettingsService
{
    Task<AiProviderOptions> GetAiProviderOptionsAsync(CancellationToken cancellationToken = default);
    Task<TelegramBotOptions> GetTelegramBotOptionsAsync(CancellationToken cancellationToken = default);
    Task<DataSourceOptions> GetDataSourceOptionsAsync(CancellationToken cancellationToken = default);
    Task<PushNotificationOptions> GetPushNotificationOptionsAsync(CancellationToken cancellationToken = default);
    Task<IReadOnlyDictionary<string, AppSetting>> GetAllAsync(CancellationToken cancellationToken = default);
    Task SaveAsync(IEnumerable<AppSettingUpdate> updates, CancellationToken cancellationToken = default);
}

public sealed record AppSettingUpdate(string Key, string? Value, bool IsSecret = false);
