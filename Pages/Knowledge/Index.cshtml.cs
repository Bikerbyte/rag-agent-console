using RagAgentConsole.Data;
using RagAgentConsole.Models;
using RagAgentConsole.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;

namespace RagAgentConsole.Pages.Knowledge;

public class IndexModel(
    ApplicationDbContext dbContext,
    IKnowledgeDocumentIngestionService knowledgeIngestionService,
    IRagRetrievalService retrievalService,
    IAppSettingsService appSettingsService) : PageModel
{
    public IReadOnlyList<KnowledgeDocument> ManagedDocuments { get; private set; } = [];
    public IReadOnlyList<RetrievalResult> RetrievalResults { get; private set; } = [];
    public int ChunkCount { get; private set; }
    public int ManagedDocumentCount { get; private set; }
    public int EstimatedTokenCount { get; private set; }
    public string? RetrievalQuery { get; private set; }
    public string? RetrievalModule { get; private set; }
    public int RetrievalTopK { get; private set; } = 5;
    public string? RetrievalError { get; private set; }
    public string AgentName { get; private set; } = "AI Assistant";
    public KnowledgeDocument? SelectedDocument { get; private set; }
    public string? SelectedDocumentSample { get; private set; }

    [BindProperty]
    public FileKnowledgeInput FileInput { get; set; } = new();

    [BindProperty]
    public List<int> SelectedDocumentIds { get; set; } = [];

    [TempData]
    public string? StatusMessage { get; set; }

    public async Task OnGetAsync(
        string? retrievalQuery = null,
        string? retrievalModule = null,
        string retrievalMode = RetrievalModes.Hybrid,
        int topK = 5,
        int? doc = null,
        CancellationToken cancellationToken = default)
    {
        RetrievalQuery = retrievalQuery;
        RetrievalModule = Request.Query.ContainsKey("retrievalModule")
            ? NormalizeModuleFilter(retrievalModule)
            : KnowledgeModuleNames.InternalDocs;
        RetrievalTopK = Math.Clamp(topK, 1, 10);
        AgentName = (await appSettingsService.GetAgentOptionsAsync(cancellationToken)).AgentName;

        ManagedDocuments = await dbContext.KnowledgeDocuments
            .AsNoTracking()
            .OrderByDescending(document => document.CreatedTime)
            .ToListAsync(cancellationToken);

        if (doc is int selectedDocumentId)
        {
            SelectedDocument = ManagedDocuments.FirstOrDefault(document => document.KnowledgeDocumentId == selectedDocumentId);
            if (SelectedDocument is not null)
            {
                SelectedDocumentSample = BuildSample(SelectedDocument.ExtractedText);
            }
        }

        if (!string.IsNullOrWhiteSpace(RetrievalQuery))
        {
            try
            {
                var response = await retrievalService.SearchWithTraceAsync(
                    RetrievalQuery,
                    maxResults: RetrievalTopK,
                    moduleName: RetrievalModule,
                    retrievalMode: retrievalMode,
                    cancellationToken: cancellationToken);
                RetrievalResults = response.Results;
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                RetrievalError = exception.Message;
            }
        }

        ManagedDocumentCount = ManagedDocuments.Count;
        ChunkCount = await dbContext.KnowledgeDocumentChunks.CountAsync(cancellationToken);
        EstimatedTokenCount = await dbContext.KnowledgeDocumentChunks
            .Select(chunk => (int?)(chunk.ChunkText.Length / 4))
            .SumAsync(cancellationToken) ?? 0;
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
                    uploads.Count == 1 && !string.IsNullOrWhiteSpace(FileInput.Title)
                        ? FileInput.Title
                        : Path.GetFileNameWithoutExtension(file.FileName),
                    file.FileName,
                    file.ContentType,
                    stream,
                    KnowledgeModuleNames.Normalize(FileInput.ModuleName),
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

        return RedirectToPage(new { section = "documents" });
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

    public async Task<IActionResult> OnPostBulkDocumentsAsync(string action, CancellationToken cancellationToken)
    {
        var ids = SelectedDocumentIds.Distinct().ToList();
        if (ids.Count == 0)
        {
            StatusMessage = "請先勾選要操作的文件。";
            return RedirectToPage(new { section = "documents" });
        }

        StatusMessage = action switch
        {
            "enable" => $"已啟用 {await knowledgeIngestionService.SetEnabledManyAsync(ids, true, cancellationToken)} 份文件。",
            "disable" => $"已停用 {await knowledgeIngestionService.SetEnabledManyAsync(ids, false, cancellationToken)} 份文件。",
            "delete" => $"已刪除 {await knowledgeIngestionService.DeleteManyAsync(ids, cancellationToken)} 份文件。",
            _ => "不支援的批次操作。"
        };

        return RedirectToPage(new { section = "documents" });
    }

    private static string? NormalizeModuleFilter(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        return KnowledgeModuleNames.Normalize(value);
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

        return parts.Count == 0 ? "沒有可匯入的檔案。" : string.Join(' ', parts);
    }

    private static string BuildSample(string? value)
    {
        var compact = string.Join(' ', (value ?? string.Empty)
            .Split(['\r', '\n', '\t'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));
        return compact.Length <= 700 ? compact : compact[..700] + "…";
    }

    public class FileKnowledgeInput
    {
        public string? Title { get; set; }
        public string? Description { get; set; }
        public string? Tags { get; set; }
        public string ModuleName { get; set; } = KnowledgeModuleNames.InternalDocs;
        public List<IFormFile> Uploads { get; set; } = [];
    }
}
