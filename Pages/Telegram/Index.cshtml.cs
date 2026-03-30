using CPBLLineBotCloud.Data;
using CPBLLineBotCloud.Models;
using CPBLLineBotCloud.Services;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace CPBLLineBotCloud.Pages.Telegram;

public class IndexModel(
    IOptions<TelegramBotOptions> options,
    ITelegramBotClient telegramBotClient,
    ApplicationDbContext dbContext,
    ILogger<IndexModel> logger) : PageModel
{
    public bool BotEnabled { get; private set; }
    public bool HasBotToken { get; private set; }
    public TelegramBotProfile? BotProfile { get; private set; }
    public string? BotStatusMessage { get; private set; }
    public IReadOnlyList<TelegramChatSubscription> RecentChats { get; private set; } = [];

    public async Task OnGetAsync()
    {
        var telegramBotOptions = options.Value;
        BotEnabled = telegramBotOptions.Enabled;
        HasBotToken = !string.IsNullOrWhiteSpace(telegramBotOptions.BotToken);

        if (BotEnabled && HasBotToken)
        {
            try
            {
                BotProfile = await telegramBotClient.GetMeAsync();
                BotStatusMessage = BotProfile is null
                    ? "Bot token 已經設定，但 getMe 沒有回傳有效的 bot profile"
                    : "Telegram bot 目前可正常連線";
            }
            catch (Exception exception)
            {
                logger.LogWarning(exception, "Failed to query Telegram getMe.");
                BotStatusMessage = "Telegram getMe 失敗，請檢查 bot token 與網路連線";
            }
        }
        else
        {
            BotStatusMessage = "Telegram bot 尚未啟用，先把 bot token 加進 user-secrets";
        }

        RecentChats = await dbContext.TelegramChatSubscriptions
            .OrderByDescending(chat => chat.LastUpdatedTime)
            .Take(10)
            .ToListAsync();
    }

    public string GetFollowedTeamLabel(TelegramChatSubscription chat)
    {
        return string.IsNullOrWhiteSpace(chat.FollowedTeamCode)
            ? "Neutral"
            : CpblTeamCatalog.GetDisplayName(chat.FollowedTeamCode);
    }
}
