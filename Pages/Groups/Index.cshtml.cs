using System.ComponentModel.DataAnnotations;
using CPBLLineBotCloud.Data;
using CPBLLineBotCloud.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;

namespace CPBLLineBotCloud.Pages.Groups;

public class IndexModel(ApplicationDbContext dbContext, ILogger<IndexModel> logger) : PageModel
{
    public IReadOnlyList<TelegramChatSubscription> Subscriptions { get; private set; } = [];
    public IReadOnlyList<SelectItem> TeamOptions { get; } = BuildTeamOptions();

    [BindProperty]
    public ChatSubscriptionInput Input { get; set; } = new();

    [TempData]
    public string? StatusMessage { get; set; }

    public bool IsEditMode => Input.TelegramChatSubscriptionId.HasValue;

    public async Task OnGetAsync(int? editId = null)
    {
        await LoadSubscriptionsAsync();

        if (editId.HasValue)
        {
            await LoadInputAsync(editId.Value);
        }
    }

    public async Task<IActionResult> OnPostSaveAsync()
    {
        if (!ModelState.IsValid)
        {
            await LoadSubscriptionsAsync();
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
        entity.EnableSchedulePush = Input.EnableSchedulePush;
        entity.EnableNewsPush = Input.EnableNewsPush;
        entity.FollowedTeamCode = string.IsNullOrWhiteSpace(Input.FollowedTeamCode) ? null : Input.FollowedTeamCode;
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

    private async Task LoadSubscriptionsAsync()
    {
        Subscriptions = await dbContext.TelegramChatSubscriptions
            .OrderBy(chat => chat.ChatTitle)
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
            EnableSchedulePush = entity.EnableSchedulePush,
            EnableNewsPush = entity.EnableNewsPush,
            FollowedTeamCode = entity.FollowedTeamCode
        };
    }

    private static IReadOnlyList<SelectItem> BuildTeamOptions()
    {
        return
        [
            new SelectItem(string.Empty, "不預設"),
            new SelectItem("CT", CpblTeamCatalog.GetDisplayName("CT")),
            new SelectItem("UL", CpblTeamCatalog.GetDisplayName("UL")),
            new SelectItem("RA", CpblTeamCatalog.GetDisplayName("RA")),
            new SelectItem("FG", CpblTeamCatalog.GetDisplayName("FG")),
            new SelectItem("WD", CpblTeamCatalog.GetDisplayName("WD")),
            new SelectItem("TS", CpblTeamCatalog.GetDisplayName("TS"))
        ];
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

        public bool EnableSchedulePush { get; set; }
        public bool EnableNewsPush { get; set; }
        public string? FollowedTeamCode { get; set; }
    }

    public sealed record SelectItem(string Value, string Label);
}
