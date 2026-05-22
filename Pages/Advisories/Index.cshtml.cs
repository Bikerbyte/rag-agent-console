using SecurityAdvisoryBot.Data;
using SecurityAdvisoryBot.Models;
using SecurityAdvisoryBot.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;

namespace SecurityAdvisoryBot.Pages.Advisories;

public class IndexModel(
    ApplicationDbContext dbContext,
    ISecurityAdvisorySyncService syncService,
    IKnowledgeDocumentIngestionService knowledgeIngestionService,
    ISecurityAdvisorySearchService searchService) : PageModel
{
    public IReadOnlyList<SecurityAdvisory> Advisories { get; private set; } = [];
    public IReadOnlyList<SecurityAdvisoryChunk> PreviewChunks { get; private set; } = [];
    public IReadOnlyList<KnowledgeDocument> ManagedDocuments { get; private set; } = [];
    public IReadOnlyList<SecurityAdvisorySearchResult> RetrievalResults { get; private set; } = [];
    public int TotalCount { get; private set; }
    public int KevCount { get; private set; }
    public int CriticalCount { get; private set; }
    public int ChunkCount { get; private set; }
    public int ManagedDocumentCount { get; private set; }
    public int EstimatedTokenCount { get; private set; }
    public string? RetrievalQuery { get; private set; }
    public int RetrievalTopK { get; private set; } = 5;
    public string? RetrievalError { get; private set; }

    [BindProperty]
    public ManualKnowledgeInput ManualInput { get; set; } = new();

    [BindProperty]
    public FileKnowledgeInput FileInput { get; set; } = new();

    [TempData]
    public string? StatusMessage { get; set; }

    public async Task OnGetAsync(
        string? q = null,
        bool kev = false,
        bool critical = false,
        string? retrievalQuery = null,
        int topK = 5,
        CancellationToken cancellationToken = default)
    {
        RetrievalQuery = retrievalQuery;
        RetrievalTopK = Math.Clamp(topK, 1, 10);

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

        ManagedDocuments = await dbContext.KnowledgeDocuments
            .AsNoTracking()
            .OrderByDescending(document => document.LastUpdatedTime)
            .Take(12)
            .ToListAsync(cancellationToken);

        if (!string.IsNullOrWhiteSpace(RetrievalQuery))
        {
            try
            {
                RetrievalResults = await searchService.SearchAsync(RetrievalQuery, RetrievalTopK, cancellationToken);
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                RetrievalError = exception.Message;
            }
        }

        TotalCount = await dbContext.SecurityAdvisories.CountAsync(cancellationToken);
        KevCount = await dbContext.SecurityAdvisories.CountAsync(advisory => advisory.IsKnownExploited, cancellationToken);
        CriticalCount = await dbContext.SecurityAdvisories.CountAsync(advisory => advisory.CvssScore >= 9 || advisory.Severity == "CRITICAL" || advisory.Severity == "Critical", cancellationToken);
        ChunkCount = await dbContext.SecurityAdvisoryChunks.CountAsync(cancellationToken);
        ManagedDocumentCount = await dbContext.KnowledgeDocuments.CountAsync(cancellationToken);
        EstimatedTokenCount = await dbContext.SecurityAdvisoryChunks
            .Select(chunk => chunk.ChunkText.Length / 4)
            .SumAsync(cancellationToken);
    }

    public async Task<IActionResult> OnPostSyncAsync(CancellationToken cancellationToken)
    {
        var result = await syncService.SyncAsync(cancellationToken);
        StatusMessage = $"Security advisory sync completed. Fetched {result.FetchedCount}, added {result.AddedCount}, updated {result.UpdatedCount}, indexed {result.ChunkCount} chunks.";
        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostImportTextAsync(CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(ManualInput.Title) || string.IsNullOrWhiteSpace(ManualInput.Text))
        {
            StatusMessage = "Title and content are required.";
            return RedirectToPage();
        }

        var result = await knowledgeIngestionService.ImportTextAsync(
            new KnowledgeDocumentImportRequest(
                ManualInput.Title,
                ManualInput.Text,
                ManualInput.ModuleName,
                "ManualText",
                ManualInput.Vendor,
                ManualInput.Product,
                ManualInput.Tags),
            cancellationToken);

        StatusMessage = $"Knowledge document imported. Title: {result.Title}. Chunks: {result.ChunkCount}.";
        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostUploadFileAsync(CancellationToken cancellationToken)
    {
        if (FileInput.Upload is null || FileInput.Upload.Length == 0)
        {
            StatusMessage = "Please choose a file to upload.";
            return RedirectToPage();
        }

        if (FileInput.Upload.Length > 15 * 1024 * 1024)
        {
            StatusMessage = "File is too large. Maximum size is 15 MB.";
            return RedirectToPage();
        }

        await using var stream = FileInput.Upload.OpenReadStream();
        var result = await knowledgeIngestionService.ImportFileAsync(
            new KnowledgeDocumentFileImportRequest(
                string.IsNullOrWhiteSpace(FileInput.Title) ? Path.GetFileNameWithoutExtension(FileInput.Upload.FileName) : FileInput.Title,
                FileInput.Upload.FileName,
                FileInput.Upload.ContentType,
                stream,
                FileInput.ModuleName,
                FileInput.Vendor,
                FileInput.Product,
                FileInput.Tags),
            cancellationToken);

        StatusMessage = $"Knowledge file imported. Title: {result.Title}. Chunks: {result.ChunkCount}.";
        return RedirectToPage();
    }

    public class ManualKnowledgeInput
    {
        public string ModuleName { get; set; } = KnowledgeModuleNames.InternalDocs;
        public string? Title { get; set; }
        public string? Vendor { get; set; }
        public string? Product { get; set; }
        public string? Tags { get; set; }
        public string? Text { get; set; }
    }

    public class FileKnowledgeInput
    {
        public string ModuleName { get; set; } = KnowledgeModuleNames.InternalDocs;
        public string? Title { get; set; }
        public string? Vendor { get; set; }
        public string? Product { get; set; }
        public string? Tags { get; set; }
        public IFormFile? Upload { get; set; }
    }
}
