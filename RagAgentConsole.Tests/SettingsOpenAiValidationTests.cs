using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using RagAgentConsole.Models;
using RagAgentConsole.Pages.Settings;
using RagAgentConsole.Services;
using Xunit;

namespace RagAgentConsole.Tests;

public class SettingsOpenAiValidationTests
{
    [Fact]
    public async Task OnPostSaveAsync_WhenNewApiKeyIsInvalid_DoesNotSaveSettings()
    {
        var settings = new FakeAppSettingsService();
        var model = new IndexModel(
            settings,
            new FakeOpenAiCredentialValidator(new OpenAiCredentialValidationResult(
                OpenAiCredentialValidationStatus.InvalidApiKey,
                "invalid key",
                [])))
        {
            Input = BuildInput("sk-invalid")
        };

        var result = await model.OnPostSaveAsync(CancellationToken.None);

        Assert.IsType<PageResult>(result);
        Assert.Null(settings.SavedUpdates);
        Assert.Null(model.Input.OpenAiApiKey);
        Assert.True(model.ModelState.ContainsKey("Input.OpenAiApiKey"));
    }

    [Fact]
    public async Task OnPostSaveAsync_WhenNewApiKeyIsValid_SavesValidatedKey()
    {
        var settings = new FakeAppSettingsService();
        var model = new IndexModel(
            settings,
            new FakeOpenAiCredentialValidator(new OpenAiCredentialValidationResult(
                OpenAiCredentialValidationStatus.Valid,
                "valid",
                [])))
        {
            Input = BuildInput(" sk-valid ")
        };

        var result = await model.OnPostSaveAsync(CancellationToken.None);

        Assert.IsType<RedirectToPageResult>(result);
        Assert.NotNull(settings.SavedUpdates);
        var savedKey = Assert.Single(settings.SavedUpdates, update => update.Key == "AiProvider:OpenAiApiKey");
        Assert.Equal("sk-valid", savedKey.Value);
        Assert.True(savedKey.IsSecret);
    }

    private static IndexModel.AppSettingsInput BuildInput(string apiKey)
        => new()
        {
            OpenAiApiKey = apiKey
        };

    private sealed class FakeOpenAiCredentialValidator(OpenAiCredentialValidationResult result)
        : IOpenAiCredentialValidator
    {
        public Task<OpenAiCredentialValidationResult> ValidateAsync(
            OpenAiCredentialValidationRequest request,
            CancellationToken cancellationToken = default)
            => Task.FromResult(result);
    }

    private sealed class FakeAppSettingsService : IAppSettingsService
    {
        public IReadOnlyList<AppSettingUpdate>? SavedUpdates { get; private set; }

        public Task<AiProviderOptions> GetAiProviderOptionsAsync(CancellationToken cancellationToken = default)
            => Task.FromResult(new AiProviderOptions());

        public Task<TelegramBotOptions> GetTelegramBotOptionsAsync(CancellationToken cancellationToken = default)
            => Task.FromResult(new TelegramBotOptions());

        public Task<DataSourceOptions> GetDataSourceOptionsAsync(CancellationToken cancellationToken = default)
            => Task.FromResult(new DataSourceOptions());

        public Task<PushNotificationOptions> GetPushNotificationOptionsAsync(CancellationToken cancellationToken = default)
            => Task.FromResult(new PushNotificationOptions());

        public Task<VectorStoreOptions> GetVectorStoreOptionsAsync(CancellationToken cancellationToken = default)
            => Task.FromResult(new VectorStoreOptions());

        public Task<ObservabilityOptions> GetObservabilityOptionsAsync(CancellationToken cancellationToken = default)
            => Task.FromResult(new ObservabilityOptions());

        public Task<AgentOptions> GetAgentOptionsAsync(CancellationToken cancellationToken = default)
            => Task.FromResult(new AgentOptions());

        public Task<IReadOnlyDictionary<string, AppSetting>> GetAllAsync(CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyDictionary<string, AppSetting>>(
                new Dictionary<string, AppSetting>());

        public Task SaveAsync(
            IEnumerable<AppSettingUpdate> updates,
            CancellationToken cancellationToken = default)
        {
            SavedUpdates = updates.ToList();
            return Task.CompletedTask;
        }
    }
}
