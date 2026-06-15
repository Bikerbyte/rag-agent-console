using RagAgentConsole.Data;
using RagAgentConsole.Models;
using RagAgentConsole.Services;
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
    public bool BotEnabled { get; private set; }
    public bool HasBotToken { get; private set; }
    public string BotStatusMessage { get; private set; } = string.Empty;
    public string AiProviderText { get; private set; } = string.Empty;
    public bool IsAiChatEnabled { get; private set; }
    public TelegramBotProfile? BotProfile { get; private set; }
    public int PendingUpdateCount { get; private set; }
    public string InstanceName { get; private set; } = string.Empty;
    public IReadOnlyList<TelegramChatSubscription> Chats { get; private set; } = [];
    public IReadOnlyList<PushLog> DeliveryLogs { get; private set; } = [];

    public async Task OnGetAsync(CancellationToken cancellationToken)
    {
        var telegram = await appSettingsService.GetTelegramBotOptionsAsync(cancellationToken);
        var ai = await appSettingsService.GetAiProviderOptionsAsync(cancellationToken);

        InstanceName = runtimeOptions.Value.InstanceName;
        AiProviderText = ai.Provider;
        IsAiChatEnabled = ai.EnableChatGeneration &&
            !string.Equals(ai.Provider, AiProviderNames.Local, StringComparison.OrdinalIgnoreCase);
        BotEnabled = telegram.Enabled;
        HasBotToken = !string.IsNullOrWhiteSpace(telegram.BotToken);
        BotStatusMessage = "Telegram bot is not enabled yet.";

        if (BotEnabled && HasBotToken)
        {
            try
            {
                BotProfile = await telegramBotClient.GetMeAsync(cancellationToken);
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

        PendingUpdateCount = await dbContext.TelegramUpdateInboxes
            .CountAsync(item => item.Status == "Pending" || item.Status == "Processing", cancellationToken);
        Chats = await dbContext.TelegramChatSubscriptions
            .AsNoTracking()
            .OrderByDescending(chat => chat.LastUpdatedTime)
            .Take(12)
            .ToListAsync(cancellationToken);
        DeliveryLogs = await dbContext.PushLogs
            .AsNoTracking()
            .OrderByDescending(log => log.CreatedTime)
            .Take(12)
            .ToListAsync(cancellationToken);
    }
}
