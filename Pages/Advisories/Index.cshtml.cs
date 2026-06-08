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
    ISecurityAdvisorySearchService searchService,
    IAppSettingsService appSettingsService) : PageModel
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
    public string AgentName { get; private set; } = "AI Assistant";
    public KnowledgeDocument? SelectedDocument { get; private set; }
    public string? SelectedDocumentSample { get; private set; }

    [BindProperty]
    public FileKnowledgeInput FileInput { get; set; } = new();

    [TempData]
    public string? StatusMessage { get; set; }

    public async Task OnGetAsync(
        string? q = null,
        bool kev = false,
        bool critical = false,
        string? retrievalQuery = null,
        string? retrievalModule = null,
        string retrievalMode = RetrievalModes.Hybrid,
        int topK = 5,
        int? doc = null,
        CancellationToken cancellationToken = default)
    {
        RetrievalQuery = retrievalQuery;
        RetrievalTopK = Math.Clamp(topK, 1, 10);
        AgentName = (await appSettingsService.GetAgentOptionsAsync(cancellationToken)).AgentName;

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

        if (doc is int selectedDocumentId)
        {
            SelectedDocument = await dbContext.KnowledgeDocuments
                .AsNoTracking()
                .FirstOrDefaultAsync(document => document.KnowledgeDocumentId == selectedDocumentId, cancellationToken);

            if (SelectedDocument is not null)
            {
                SelectedDocumentSample = BuildSample(SelectedDocument.ExtractedText);
            }
        }

        if (!string.IsNullOrWhiteSpace(RetrievalQuery))
        {
            try
            {
                var response = await searchService.SearchWithTraceAsync(
                    RetrievalQuery,
                    maxResults: RetrievalTopK,
                    moduleName: NormalizeModuleFilter(retrievalModule),
                    retrievalMode: retrievalMode,
                    cancellationToken: cancellationToken);
                RetrievalResults = response.Results;
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
        StatusMessage = $"範例連接器同步完成：取得 {result.FetchedCount} 筆、新增 {result.AddedCount} 筆、更新 {result.UpdatedCount} 筆、索引 {result.ChunkCount} 個片段。";
        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostUploadFileAsync(CancellationToken cancellationToken)
    {
        if (FileInput.Upload is null || FileInput.Upload.Length == 0)
        {
            StatusMessage = "請選擇要上傳的檔案。";
            return RedirectToPage();
        }

        if (FileInput.Upload.Length > 15 * 1024 * 1024)
        {
            StatusMessage = "檔案過大，上限為 15 MB。";
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

        StatusMessage = $"檔案匯入完成：{result.Title}，共 {result.ChunkCount} 個片段。";
        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostToggleDocumentAsync(int id, bool enabled, CancellationToken cancellationToken)
    {
        await knowledgeIngestionService.SetEnabledAsync(id, enabled, cancellationToken);
        StatusMessage = enabled ? "文件已啟用。" : "文件已停用。";
        return RedirectToPage(new { section = "documents" });
    }

    public async Task<IActionResult> OnPostReindexDocumentAsync(int id, CancellationToken cancellationToken)
    {
        var result = await knowledgeIngestionService.RebuildEmbeddingsAsync(id, cancellationToken);
        StatusMessage = $"文件已重新索引：{result.Title}，共 {result.ChunkCount} 個片段。";
        return RedirectToPage(new { section = "documents" });
    }

    public async Task<IActionResult> OnPostDeleteDocumentAsync(int id, CancellationToken cancellationToken)
    {
        await knowledgeIngestionService.DeleteAsync(id, cancellationToken);
        StatusMessage = "文件已刪除。";
        return RedirectToPage(new { section = "documents" });
    }

    private static string? NormalizeModuleFilter(string? value)
        => value switch
        {
            KnowledgeModuleNames.CveAdvisory => KnowledgeModuleNames.CveAdvisory,
            KnowledgeModuleNames.WorkflowQa => KnowledgeModuleNames.WorkflowQa,
            KnowledgeModuleNames.InternalDocs => KnowledgeModuleNames.InternalDocs,
            _ => null
        };

    private static string BuildSample(string? value)
    {
        var compact = string.Join(' ', (value ?? string.Empty)
            .Split(new[] { '\r', '\n', '\t' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));
        return compact.Length <= 700 ? compact : compact[..700] + "…";
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
