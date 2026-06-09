using RagAgentConsole.Data;
using RagAgentConsole.Models;
using RagAgentConsole.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;

namespace RagAgentConsole.Pages.Advisories;

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

    // 知識庫已合併為單一來源清單：未選取任何文件時，預設顯示「資安公告自動同步」這個系統託管來源的明細。
    public bool CveSourceSelected => SelectedDocument is null;

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
        var uploads = (FileInput.Uploads ?? [])
            .Where(file => file is { Length: > 0 })
            .ToList();

        if (uploads.Count == 0)
        {
            StatusMessage = "請選擇要上傳的檔案。";
            return RedirectToPage();
        }

        var failures = new List<string>();
        var requests = new List<KnowledgeDocumentFileImportRequest>();
        var streams = new List<Stream>();

        try
        {
            foreach (var file in uploads)
            {
                if (file.Length > 15 * 1024 * 1024)
                {
                    failures.Add($"{file.FileName}（超過 15 MB）");
                    continue;
                }

                var stream = file.OpenReadStream();
                streams.Add(stream);
                requests.Add(new KnowledgeDocumentFileImportRequest(
                    // 批次上傳時不套用單一標題欄位，改用各自的檔名；只有單檔時才採用使用者填的標題。
                    uploads.Count == 1 && !string.IsNullOrWhiteSpace(FileInput.Title)
                        ? FileInput.Title
                        : Path.GetFileNameWithoutExtension(file.FileName),
                    file.FileName,
                    file.ContentType,
                    stream,
                    KnowledgeModuleNames.InternalDocs,
                    Vendor: null,
                    Product: null,
                    Tags: FileInput.Tags,
                    Description: FileInput.Description));
            }

            var importedCount = 0;
            var chunkTotal = 0;
            if (requests.Count > 0)
            {
                var result = await knowledgeIngestionService.ImportFilesAsync(requests, cancellationToken);
                importedCount = result.Imported.Count;
                chunkTotal = result.Imported.Sum(item => item.ChunkCount);
                failures.AddRange(result.Failures.Select(failure => $"{failure.FileName}（{failure.Error}）"));
            }

            StatusMessage = BuildUploadStatus(importedCount, chunkTotal, failures);
        }
        finally
        {
            foreach (var stream in streams)
            {
                stream.Dispose();
            }
        }

        return RedirectToPage();
    }

    private static string BuildUploadStatus(int importedCount, int chunkTotal, IReadOnlyList<string> failures)
    {
        var parts = new List<string>();
        if (importedCount > 0)
        {
            parts.Add($"已匯入 {importedCount} 份文件，共 {chunkTotal} 個片段。");
        }

        if (failures.Count > 0)
        {
            parts.Add($"略過 {failures.Count} 個檔案：{string.Join("、", failures)}");
        }

        return parts.Count == 0 ? "沒有可匯入的檔案。" : string.Join(" ", parts);
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
        public string? Title { get; set; }
        public string? Description { get; set; }
        public string? Tags { get; set; }
        public List<IFormFile> Uploads { get; set; } = [];
    }
}
