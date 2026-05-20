using CPBLLineBotCloud.Data;
using CPBLLineBotCloud.Models;
using CPBLLineBotCloud.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;

namespace CPBLLineBotCloud.Pages.Advisories;

public class IndexModel(ApplicationDbContext dbContext, ISecurityAdvisorySyncService syncService) : PageModel
{
    public IReadOnlyList<SecurityAdvisory> Advisories { get; private set; } = [];
    public int TotalCount { get; private set; }
    public int KevCount { get; private set; }
    public int CriticalCount { get; private set; }
    public int ChunkCount { get; private set; }

    [TempData]
    public string? StatusMessage { get; set; }

    public async Task OnGetAsync(string? q = null, bool kev = false, bool critical = false)
    {
        var query = dbContext.SecurityAdvisories.AsQueryable();

        if (!string.IsNullOrWhiteSpace(q))
        {
            var keyword = q.Trim().ToLowerInvariant();
            query = query.Where(advisory =>
                (advisory.CveId != null && advisory.CveId.ToLower().Contains(keyword)) ||
                advisory.Title.ToLower().Contains(keyword) ||
                advisory.Description.ToLower().Contains(keyword) ||
                (advisory.Vendor != null && advisory.Vendor.ToLower().Contains(keyword)) ||
                (advisory.Product != null && advisory.Product.ToLower().Contains(keyword)) ||
                (advisory.Tags != null && advisory.Tags.ToLower().Contains(keyword)));
        }

        if (kev)
        {
            query = query.Where(advisory => advisory.IsKnownExploited);
        }

        if (critical)
        {
            query = query.Where(advisory => advisory.CvssScore >= 9 || advisory.Severity == "CRITICAL" || advisory.Severity == "Critical");
        }

        Advisories = await query
            .OrderByDescending(advisory => advisory.LastModifiedAt ?? advisory.PublishedAt ?? advisory.LastSyncedTime)
            .Take(100)
            .ToListAsync();

        TotalCount = await dbContext.SecurityAdvisories.CountAsync();
        KevCount = await dbContext.SecurityAdvisories.CountAsync(advisory => advisory.IsKnownExploited);
        CriticalCount = await dbContext.SecurityAdvisories.CountAsync(advisory => advisory.CvssScore >= 9 || advisory.Severity == "CRITICAL" || advisory.Severity == "Critical");
        ChunkCount = await dbContext.SecurityAdvisoryChunks.CountAsync();
    }

    public async Task<IActionResult> OnPostSyncAsync(CancellationToken cancellationToken)
    {
        var result = await syncService.SyncAsync(cancellationToken);
        StatusMessage = $"Security advisory sync completed. Fetched {result.FetchedCount}, added {result.AddedCount}, updated {result.UpdatedCount}, indexed {result.ChunkCount} chunks.";
        return RedirectToPage();
    }
}
