using CPBLLineBotCloud.Data;
using CPBLLineBotCloud.Models;
using CPBLLineBotCloud.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;

namespace CPBLLineBotCloud.Pages.News;

public class IndexModel(ApplicationDbContext dbContext, IBaseballNewsSyncService baseballNewsSyncService) : PageModel
{
    public IReadOnlyList<NewsInfo> NewsItems { get; private set; } = [];

    [TempData]
    public string? StatusMessage { get; set; }

    public async Task OnGetAsync()
    {
        NewsItems = await dbContext.NewsItems
            .OrderByDescending(news => news.PublishTime)
            .Take(100)
            .ToListAsync();
    }

    public async Task<IActionResult> OnPostSyncAsync(CancellationToken cancellationToken)
    {
        var count = await baseballNewsSyncService.SyncAsync(cancellationToken);
        StatusMessage = $"已完成新聞同步，新增 {count} 筆官方新聞。";
        return RedirectToPage();
    }
}
