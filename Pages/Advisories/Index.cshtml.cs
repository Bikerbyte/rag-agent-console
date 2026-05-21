using SecurityAdvisoryBot.Data;
using SecurityAdvisoryBot.Models;
using SecurityAdvisoryBot.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;

namespace SecurityAdvisoryBot.Pages.Advisories;

public class IndexModel(ApplicationDbContext dbContext, ISecurityAdvisorySyncService syncService) : PageModel
{
    public IReadOnlyList<SecurityAdvisory> Advisories { get; private set; } = [];
    public IReadOnlyList<SecurityAdvisoryChunk> PreviewChunks { get; private set; } = [];
    public int TotalCount { get; private set; }
    public int KevCount { get; private set; }
    public int CriticalCount { get; private set; }
    public int ChunkCount { get; private set; }
    public int EstimatedTokenCount { get; private set; }

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
            .Take(60)
            .ToListAsync();

        PreviewChunks = await dbContext.SecurityAdvisoryChunks
            .Include(chunk => chunk.Advisory)
            .OrderByDescending(chunk => chunk.SecurityAdvisoryChunkId)
            .Take(5)
            .ToListAsync();

        TotalCount = await dbContext.SecurityAdvisories.CountAsync();
        KevCount = await dbContext.SecurityAdvisories.CountAsync(advisory => advisory.IsKnownExploited);
        CriticalCount = await dbContext.SecurityAdvisories.CountAsync(advisory => advisory.CvssScore >= 9 || advisory.Severity == "CRITICAL" || advisory.Severity == "Critical");
        ChunkCount = await dbContext.SecurityAdvisoryChunks.CountAsync();
        EstimatedTokenCount = await dbContext.SecurityAdvisoryChunks
            .Select(chunk => chunk.ChunkText.Length / 4)
            .SumAsync();
    }

    public async Task<IActionResult> OnPostSyncAsync(CancellationToken cancellationToken)
    {
        var result = await syncService.SyncAsync(cancellationToken);
        StatusMessage = $"Security advisory sync completed. Fetched {result.FetchedCount}, added {result.AddedCount}, updated {result.UpdatedCount}, indexed {result.ChunkCount} chunks.";
        return RedirectToPage();
    }
}
