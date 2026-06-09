using System.ComponentModel.DataAnnotations;
using RagAgentConsole.Data;
using RagAgentConsole.Models;
using RagAgentConsole.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace RagAgentConsole.Pages.Operations;

public class IndexModel(
    ApplicationDbContext dbContext,
    ITelegramBotClient telegramBotClient,
    IOptions<AppRuntimeOptions> runtimeOptions,
    IAppSettingsService appSettingsService,
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

    [BindProperty]
    public ChatSubscriptionInput Input { get; set; } = new();

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

    private async Task LoadPageDataAsync()
    {
        var currentTelegramOptions = await appSettingsService.GetTelegramBotOptionsAsync();
        var currentAiOptions = await appSettingsService.GetAiProviderOptionsAsync();
        var now = DateTimeOffset.UtcNow;
        var activeThreshold = now.Subtract(OfflineThreshold);

        InstanceName = runtimeOptions.Value.InstanceName;
        AiProviderText = currentAiOptions.Provider;
        IsAiChatEnabled = currentAiOptions.EnableChatGeneration &&
            !string.Equals(currentAiOptions.Provider, AiProviderNames.Local, StringComparison.OrdinalIgnoreCase);
        BotEnabled = currentTelegramOptions.Enabled;
        HasBotToken = !string.IsNullOrWhiteSpace(currentTelegramOptions.BotToken);
        BotStatusMessage = "Telegram bot is not enabled yet.";

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

    public class NodeSummaryViewModel
    {
        public required string InstanceName { get; init; }
        public required string RoleSummary { get; init; }
        public required string MachineName { get; init; }
        public DateTimeOffset LastSeenTime { get; init; }
        public required string StatusText { get; init; }
    }
}
