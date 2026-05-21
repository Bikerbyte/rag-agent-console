using System.ComponentModel.DataAnnotations;
using System.Text.Json;
using SecurityAdvisoryBot.Data;
using SecurityAdvisoryBot.Models;
using SecurityAdvisoryBot.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace SecurityAdvisoryBot.Pages.Operations;

public class IndexModel(
    ApplicationDbContext dbContext,
    ITelegramBotClient telegramBotClient,
    IOptions<AppRuntimeOptions> runtimeOptions,
    IAppSettingsService appSettingsService,
    ISecurityAdvisoryAgentService advisoryAgent,
    ILogger<IndexModel> logger) : PageModel
{
    private static readonly TimeSpan OfflineThreshold = TimeSpan.FromSeconds(45);

    public bool BotEnabled { get; private set; }
    public bool HasBotToken { get; private set; }
    public string BotStatusMessage { get; private set; } = string.Empty;
    public string AiProviderText { get; private set; } = string.Empty;
    public bool IsAiChatEnabled { get; private set; }
    public TelegramBotProfile? BotProfile { get; private set; }
    public int ActiveNodeCount { get; private set; }
    public int PendingUpdateCount { get; private set; }
    public string InstanceName { get; private set; } = string.Empty;
    public IReadOnlyList<TelegramChatSubscription> Subscriptions { get; private set; } = [];
    public IReadOnlyList<PushLog> PushLogs { get; private set; } = [];
    public IReadOnlyList<SyncJobLog> SyncJobLogs { get; private set; } = [];
    public IReadOnlyList<NodeSummaryViewModel> Nodes { get; private set; } = [];
    public IReadOnlyList<AgentChatMessageViewModel> AgentMessages { get; private set; } = [];
    public bool HasOpenAiApiKey { get; private set; }
    public bool HasTelegramBotToken { get; private set; }

    [BindProperty]
    public ChatSubscriptionInput Input { get; set; } = new();

    [BindProperty]
    public AgentTestInput AgentInput { get; set; } = new();

    [BindProperty]
    public string? AgentHistoryJson { get; set; }

    [BindProperty]
    public AppSettingsInput SettingsInput { get; set; } = new();

    [TempData]
    public string? StatusMessage { get; set; }

    public bool IsEditMode => Input.TelegramChatSubscriptionId.HasValue;

    public async Task OnGetAsync(int? editId = null)
    {
        await LoadPageDataAsync();

        if (editId.HasValue)
        {
            await LoadInputAsync(editId.Value);
        }
    }

    public async Task<IActionResult> OnPostSaveAsync()
    {
        if (!ModelState.IsValid)
        {
            await LoadPageDataAsync();
            return Page();
        }

        var now = DateTimeOffset.UtcNow;
        TelegramChatSubscription entity;

        if (Input.TelegramChatSubscriptionId.HasValue)
        {
            entity = await dbContext.TelegramChatSubscriptions
                .FirstOrDefaultAsync(chat => chat.TelegramChatSubscriptionId == Input.TelegramChatSubscriptionId.Value)
                ?? throw new InvalidOperationException("Telegram chat subscription was not found.");
        }
        else
        {
            entity = new TelegramChatSubscription
            {
                ChatId = Input.ChatId.Trim(),
                ChatTitle = Input.ChatTitle.Trim(),
                CreatedTime = now,
                LastUpdatedTime = now
            };

            await dbContext.TelegramChatSubscriptions.AddAsync(entity);
        }

        entity.ChatId = Input.ChatId.Trim();
        entity.ChatTitle = Input.ChatTitle.Trim();
        entity.EnableAdvisoryPush = Input.EnableAdvisoryPush;
        entity.AdvisoryKeywords = string.IsNullOrWhiteSpace(Input.AdvisoryKeywords) ? null : Input.AdvisoryKeywords.Trim();
        entity.MinimumSeverity = string.IsNullOrWhiteSpace(Input.MinimumSeverity) ? null : Input.MinimumSeverity.Trim();
        entity.LastUpdatedTime = now;

        await dbContext.SaveChangesAsync();
        logger.LogInformation("Saved Telegram chat subscription {ChatId}.", entity.ChatId);

        StatusMessage = Input.TelegramChatSubscriptionId.HasValue
            ? "Telegram chat settings updated."
            : "Telegram chat created.";

        return RedirectToPage();
    }

    public IActionResult OnPostEdit(int id)
    {
        return RedirectToPage(new { editId = id });
    }

    public IActionResult OnPostReset()
    {
        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostSaveSettingsAsync(CancellationToken cancellationToken)
    {
        var currentAi = await appSettingsService.GetAiProviderOptionsAsync(cancellationToken);
        var currentTelegram = await appSettingsService.GetTelegramBotOptionsAsync(cancellationToken);

        var updates = new List<AppSettingUpdate>
        {
            new("AiProvider:Provider", SettingsInput.AiProvider),
            new("AiProvider:EnableChatGeneration", SettingsInput.EnableChatGeneration.ToString()),
            new("AiProvider:UseLocalFallback", SettingsInput.UseLocalFallback.ToString()),
            new("AiProvider:OpenAiApiBaseUrl", SettingsInput.OpenAiApiBaseUrl),
            new("AiProvider:OpenAiChatModel", SettingsInput.OpenAiChatModel),
            new("AiProvider:OpenAiEmbeddingModel", SettingsInput.OpenAiEmbeddingModel),
            new("AiProvider:OllamaApiBaseUrl", SettingsInput.OllamaApiBaseUrl),
            new("AiProvider:OllamaChatModel", SettingsInput.OllamaChatModel),
            new("AiProvider:OllamaEmbeddingModel", SettingsInput.OllamaEmbeddingModel),
            new("TelegramBot:Enabled", SettingsInput.TelegramEnabled.ToString()),
            new("TelegramBot:ApiBaseUrl", SettingsInput.TelegramApiBaseUrl),
            new("TelegramBot:UseWebhookMode", SettingsInput.UseWebhookMode.ToString()),
            new("TelegramBot:WebhookUrl", SettingsInput.WebhookUrl),
            new("TelegramBot:WebhookPath", SettingsInput.WebhookPath),
            new("TelegramBot:PollingDelaySeconds", SettingsInput.PollingDelaySeconds.ToString()),
            new("DataSources:AutoSyncIntervalMinutes", SettingsInput.AutoSyncIntervalMinutes.ToString()),
            new("PushNotifications:Enabled", SettingsInput.PushEnabled.ToString()),
            new("PushNotifications:EnableSecurityAdvisoryPush", SettingsInput.SecurityAdvisoryPushEnabled.ToString()),
            new("PushNotifications:WorkerIntervalSeconds", SettingsInput.PushWorkerIntervalSeconds.ToString()),
            new("PushNotifications:AdvisoryLookbackHours", SettingsInput.AdvisoryLookbackHours.ToString())
        };

        updates.Add(new AppSettingUpdate(
            "AiProvider:OpenAiApiKey",
            string.IsNullOrWhiteSpace(SettingsInput.OpenAiApiKey) ? currentAi.OpenAiApiKey : SettingsInput.OpenAiApiKey,
            IsSecret: true));

        updates.Add(new AppSettingUpdate(
            "TelegramBot:BotToken",
            string.IsNullOrWhiteSpace(SettingsInput.TelegramBotToken) ? currentTelegram.BotToken : SettingsInput.TelegramBotToken,
            IsSecret: true));

        updates.Add(new AppSettingUpdate(
            "TelegramBot:WebhookSecretToken",
            string.IsNullOrWhiteSpace(SettingsInput.WebhookSecretToken) ? currentTelegram.WebhookSecretToken : SettingsInput.WebhookSecretToken,
            IsSecret: true));

        await appSettingsService.SaveAsync(updates, cancellationToken);
        StatusMessage = "Application settings saved. AI and Telegram clients will use the new values immediately. Worker role changes may require restart.";
        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostClearAgentAsync()
    {
        await LoadPageDataAsync();
        AgentMessages = [];
        AgentHistoryJson = null;
        AgentInput = new AgentTestInput();
        return Page();
    }

    public async Task<IActionResult> OnPostAskAgentAsync(CancellationToken cancellationToken)
    {
        await LoadPageDataAsync();
        var messages = ReadAgentHistory().ToList();

        if (string.IsNullOrWhiteSpace(AgentInput.Message))
        {
            AgentMessages = messages;
            AgentHistoryJson = JsonSerializer.Serialize(AgentMessages);
            return Page();
        }

        var history = messages
            .Select(message => new AdvisoryConversationMessage(message.Role, message.Content))
            .ToList();

        var userMessage = AgentInput.Message.Trim();
        messages.Add(new AgentChatMessageViewModel("user", userMessage));

        var reply = await advisoryAgent.BuildReplyAsync(userMessage, "operations-preview", history, cancellationToken);
        messages.Add(new AgentChatMessageViewModel("assistant", reply));

        AgentMessages = messages.TakeLast(12).ToList();
        AgentHistoryJson = JsonSerializer.Serialize(AgentMessages);
        AgentInput = new AgentTestInput();
        ModelState.Clear();
        return Page();
    }

    private async Task LoadPageDataAsync()
    {
        var currentTelegramOptions = await appSettingsService.GetTelegramBotOptionsAsync();
        var currentAiOptions = await appSettingsService.GetAiProviderOptionsAsync();
        var currentDataSourceOptions = await appSettingsService.GetDataSourceOptionsAsync();
        var currentPushOptions = await appSettingsService.GetPushNotificationOptionsAsync();
        var now = DateTimeOffset.UtcNow;
        var activeThreshold = now.Subtract(OfflineThreshold);

        InstanceName = runtimeOptions.Value.InstanceName;
        AiProviderText = currentAiOptions.Provider;
        IsAiChatEnabled = currentAiOptions.EnableChatGeneration &&
            !string.Equals(currentAiOptions.Provider, AiProviderNames.Local, StringComparison.OrdinalIgnoreCase);
        HasOpenAiApiKey = !string.IsNullOrWhiteSpace(currentAiOptions.OpenAiApiKey);
        BotEnabled = currentTelegramOptions.Enabled;
        HasBotToken = !string.IsNullOrWhiteSpace(currentTelegramOptions.BotToken);
        HasTelegramBotToken = HasBotToken;
        BotStatusMessage = "Telegram bot is not enabled yet.";
        SettingsInput = AppSettingsInput.From(currentAiOptions, currentTelegramOptions, currentDataSourceOptions, currentPushOptions);

        if (BotEnabled && HasBotToken)
        {
            try
            {
                BotProfile = await telegramBotClient.GetMeAsync();
                BotStatusMessage = BotProfile is null
                    ? "Bot token is configured, but Telegram did not return a profile."
                    : $"Connected as @{BotProfile.Username ?? BotProfile.FirstName}";
            }
            catch (Exception exception)
            {
                logger.LogWarning(exception, "Failed to query Telegram getMe.");
                BotStatusMessage = "Telegram getMe failed. Check token and network access.";
            }
        }

        ActiveNodeCount = await dbContext.RuntimeNodeHeartbeats.CountAsync(item => item.LastSeenTime >= activeThreshold);
        PendingUpdateCount = await dbContext.TelegramUpdateInboxes.CountAsync(item => item.Status == "Pending" || item.Status == "Processing");

        Subscriptions = await dbContext.TelegramChatSubscriptions
            .OrderByDescending(chat => chat.EnableAdvisoryPush)
            .ThenBy(chat => chat.ChatTitle)
            .Take(12)
            .ToListAsync();

        PushLogs = await dbContext.PushLogs
            .OrderByDescending(log => log.CreatedTime)
            .Take(8)
            .ToListAsync();

        SyncJobLogs = await dbContext.SyncJobLogs
            .OrderByDescending(log => log.StartTime)
            .Take(8)
            .ToListAsync();

        Nodes = await dbContext.RuntimeNodeHeartbeats
            .OrderByDescending(item => item.LastSeenTime)
            .Take(8)
            .Select(item => new NodeSummaryViewModel
            {
                InstanceName = item.InstanceName,
                RoleSummary = item.RoleSummary,
                MachineName = item.MachineName,
                LastSeenTime = item.LastSeenTime,
                StatusText = now - item.LastSeenTime > OfflineThreshold ? "Offline?" : "Online"
            })
            .ToListAsync();
    }

    private IReadOnlyList<AgentChatMessageViewModel> ReadAgentHistory()
    {
        if (string.IsNullOrWhiteSpace(AgentHistoryJson))
        {
            return [];
        }

        try
        {
            return JsonSerializer.Deserialize<List<AgentChatMessageViewModel>>(AgentHistoryJson) ?? [];
        }
        catch (JsonException exception)
        {
            logger.LogWarning(exception, "Failed to parse operations agent chat history.");
            return [];
        }
    }

    private async Task LoadInputAsync(int editId)
    {
        var entity = await dbContext.TelegramChatSubscriptions
            .FirstOrDefaultAsync(chat => chat.TelegramChatSubscriptionId == editId);

        if (entity is null)
        {
            StatusMessage = "Requested Telegram chat was not found.";
            return;
        }

        Input = new ChatSubscriptionInput
        {
            TelegramChatSubscriptionId = entity.TelegramChatSubscriptionId,
            ChatId = entity.ChatId,
            ChatTitle = entity.ChatTitle,
            EnableAdvisoryPush = entity.EnableAdvisoryPush,
            AdvisoryKeywords = entity.AdvisoryKeywords,
            MinimumSeverity = entity.MinimumSeverity
        };
    }

    public class ChatSubscriptionInput
    {
        public int? TelegramChatSubscriptionId { get; set; }

        [Required]
        [StringLength(64)]
        public string ChatId { get; set; } = string.Empty;

        [Required]
        [StringLength(128)]
        public string ChatTitle { get; set; } = string.Empty;

        public bool EnableAdvisoryPush { get; set; } = true;

        [StringLength(800)]
        public string? AdvisoryKeywords { get; set; }

        [StringLength(32)]
        public string? MinimumSeverity { get; set; }
    }

    public class AgentTestInput
    {
        [StringLength(800)]
        public string? Message { get; set; }
    }

    public sealed record AgentChatMessageViewModel(string Role, string Content);

    public class AppSettingsInput
    {
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

        public static AppSettingsInput From(
            AiProviderOptions ai,
            TelegramBotOptions telegram,
            DataSourceOptions dataSource,
            PushNotificationOptions push)
            => new()
            {
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
                AdvisoryLookbackHours = push.AdvisoryLookbackHours
            };
    }

    public class NodeSummaryViewModel
    {
        public required string InstanceName { get; init; }
        public required string RoleSummary { get; init; }
        public required string MachineName { get; init; }
        public DateTimeOffset LastSeenTime { get; init; }
        public required string StatusText { get; init; }
    }
}
