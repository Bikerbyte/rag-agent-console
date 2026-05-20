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

        public bool EnableAdvisoryPush { get; set; }

        [StringLength(800)]
        public string? AdvisoryKeywords { get; set; }

        [StringLength(32)]
        public string? MinimumSeverity { get; set; }
    }
}
