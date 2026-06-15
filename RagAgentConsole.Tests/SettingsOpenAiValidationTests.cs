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

    [Fact]
    public async Task OnGetAsync_WhenSecretsExist_ExposesOnlyConfiguredState()
    {
        var settings = new FakeAppSettingsService
        {
            AiOptions = new AiProviderOptions { OpenAiApiKey = "sk-existing" },
            TelegramOptions = new TelegramBotOptions
            {
                BotToken = "telegram-existing",
                WebhookSecretToken = "webhook-existing"
            }
        };
        var model = new IndexModel(
            settings,
            new FakeOpenAiCredentialValidator(new OpenAiCredentialValidationResult(
                OpenAiCredentialValidationStatus.Valid,
                "valid",
                [])));

        await model.OnGetAsync(CancellationToken.None);

        Assert.True(model.HasOpenAiApiKey);
        Assert.True(model.HasTelegramBotToken);
        Assert.True(model.HasWebhookSecretToken);
        Assert.Null(model.Input.OpenAiApiKey);
        Assert.Null(model.Input.TelegramBotToken);
        Assert.Null(model.Input.WebhookSecretToken);
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
        public AiProviderOptions AiOptions { get; init; } = new();
        public TelegramBotOptions TelegramOptions { get; init; } = new();

        public Task<AiProviderOptions> GetAiProviderOptionsAsync(CancellationToken cancellationToken = default)
            => Task.FromResult(AiOptions);

        public Task<TelegramBotOptions> GetTelegramBotOptionsAsync(CancellationToken cancellationToken = default)
            => Task.FromResult(TelegramOptions);

        public Task<RagOptions> GetRagOptionsAsync(CancellationToken cancellationToken = default)
            => Task.FromResult(new RagOptions());

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
