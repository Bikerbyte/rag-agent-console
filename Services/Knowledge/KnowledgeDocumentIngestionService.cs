using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using RagAgentConsole.Data;
using RagAgentConsole.Models;
using Microsoft.EntityFrameworkCore;

namespace RagAgentConsole.Services;

public interface IKnowledgeDocumentIngestionService
{
    Task<KnowledgeDocumentIngestionResult> ImportTextAsync(
        KnowledgeDocumentImportRequest request,
        CancellationToken cancellationToken = default);

    Task<KnowledgeDocumentIngestionResult> ImportFileAsync(
        KnowledgeDocumentFileImportRequest request,
        CancellationToken cancellationToken = default);

    Task<KnowledgeDocumentBatchImportResult> ImportFilesAsync(
        IReadOnlyList<KnowledgeDocumentFileImportRequest> requests,
        CancellationToken cancellationToken = default);

    Task SetEnabledAsync(int documentId, bool isEnabled, CancellationToken cancellationToken = default);

    /// <summary>Enable or disable several documents at once. Returns the affected count.</summary>
    Task<int> SetEnabledManyAsync(IReadOnlyList<int> documentIds, bool isEnabled, CancellationToken cancellationToken = default);

    Task DeleteAsync(int documentId, CancellationToken cancellationToken = default);

    /// <summary>Delete several documents at once. Returns the affected count.</summary>
    Task<int> DeleteManyAsync(IReadOnlyList<int> documentIds, CancellationToken cancellationToken = default);

    Task<KnowledgeDocumentIngestionResult> RebuildEmbeddingsAsync(
        int documentId,
        CancellationToken cancellationToken = default);
}

public sealed record KnowledgeDocumentImportRequest(
    string Title,
    string Text,
    string ModuleName,
    string SourceType,
    string? Vendor,
    string? Product,
    string? Tags);

public sealed record KnowledgeDocumentFileImportRequest(
    string Title,
    string FileName,
    string? ContentType,
    Stream ContentStream,
    string ModuleName,
    string? Vendor,
    string? Product,
    string? Tags,
    string? Description = null);

public sealed record KnowledgeDocumentBatchImportResult(
    IReadOnlyList<KnowledgeDocumentIngestionResult> Imported,
    IReadOnlyList<KnowledgeDocumentImportFailure> Failures);

public sealed record KnowledgeDocumentImportFailure(
    string FileName,
    string Error);

public class KnowledgeDocumentIngestionService(
    ApplicationDbContext dbContext,
    IKnowledgeDocumentTextExtractor textExtractor,
    IKnowledgeTextChunkingService chunkingService,
    IRagEmbeddingService embeddingService,
    IBm25Index bm25Index,
    ILogger<KnowledgeDocumentIngestionService> logger) : IKnowledgeDocumentIngestionService
{
    public async Task<KnowledgeDocumentIngestionResult> ImportTextAsync(
        KnowledgeDocumentImportRequest request,
        CancellationToken cancellationToken = default)
    {
        var text = NormalizeText(request.Text);
        return await SaveDocumentAsync(
            request.Title,
            request.SourceType,
            fileName: null,
            contentType: "text/plain",
            text,
            request.ModuleName,
            request.Vendor,
            request.Product,
            request.Tags,
            isMarkdown: false,
            refreshIndex: true,
            cancellationToken);
    }

    public Task<KnowledgeDocumentIngestionResult> ImportFileAsync(
        KnowledgeDocumentFileImportRequest request,
        CancellationToken cancellationToken = default)
        => ImportFileCoreAsync(request, refreshIndex: true, cancellationToken);

    public async Task<KnowledgeDocumentBatchImportResult> ImportFilesAsync(
        IReadOnlyList<KnowledgeDocumentFileImportRequest> requests,
        CancellationToken cancellationToken = default)
    {
        var imported = new List<KnowledgeDocumentIngestionResult>();
        var failures = new List<KnowledgeDocumentImportFailure>();

        // 逐檔匯入，但 BM25 索引只在最後重建一次，避免每個檔案都觸發一次整庫重建。
        foreach (var request in requests)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                imported.Add(await ImportFileCoreAsync(request, refreshIndex: false, cancellationToken));
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                logger.LogWarning(exception, "Batch import failed for file {FileName}.", request.FileName);
                failures.Add(new KnowledgeDocumentImportFailure(request.FileName, exception.Message));
            }
        }

        if (imported.Count > 0)
        {
            await TryRefreshBm25IndexAsync(cancellationToken);
        }

        return new KnowledgeDocumentBatchImportResult(imported, failures);
    }

    private async Task<KnowledgeDocumentIngestionResult> ImportFileCoreAsync(
        KnowledgeDocumentFileImportRequest request,
        bool refreshIndex,
        CancellationToken cancellationToken)
    {
        var extracted = await textExtractor.ExtractAsync(
            request.FileName,
            request.ContentType,
            request.ContentStream,
            cancellationToken);

        var isMarkdown = string.Equals(extracted.NormalizedContentType, "text/markdown", StringComparison.OrdinalIgnoreCase);
        return await SaveDocumentAsync(
            string.IsNullOrWhiteSpace(request.Title) ? Path.GetFileNameWithoutExtension(request.FileName) : request.Title,
            "UploadedFile",
            request.FileName,
            extracted.NormalizedContentType,
            BuildDocumentText(request.Description, extracted.Text),
            request.ModuleName,
            request.Vendor,
            request.Product,
            request.Tags,
            isMarkdown,
            refreshIndex,
            cancellationToken);
    }

    public async Task SetEnabledAsync(int documentId, bool isEnabled, CancellationToken cancellationToken = default)
    {
        var document = await dbContext.KnowledgeDocuments
            .FirstOrDefaultAsync(item => item.KnowledgeDocumentId == documentId, cancellationToken);
        if (document is null)
        {
            return;
        }

        document.IsEnabled = isEnabled;
        document.Status = isEnabled ? "Available" : "Disabled";
        document.LastUpdatedTime = DateTimeOffset.UtcNow;
        await dbContext.SaveChangesAsync(cancellationToken);
    }

    public async Task<int> SetEnabledManyAsync(IReadOnlyList<int> documentIds, bool isEnabled, CancellationToken cancellationToken = default)
    {
        var ids = documentIds.Distinct().ToList();
        if (ids.Count == 0)
        {
            return 0;
        }

        var documents = await dbContext.KnowledgeDocuments
            .Where(item => ids.Contains(item.KnowledgeDocumentId))
            .ToListAsync(cancellationToken);

        var now = DateTimeOffset.UtcNow;
        foreach (var document in documents)
        {
            document.IsEnabled = isEnabled;
            document.Status = isEnabled ? "Available" : "Disabled";
            document.LastUpdatedTime = now;
        }

        await dbContext.SaveChangesAsync(cancellationToken);
        return documents.Count;
    }

    public async Task<int> DeleteManyAsync(IReadOnlyList<int> documentIds, CancellationToken cancellationToken = default)
    {
        var ids = documentIds.Distinct().ToList();
        if (ids.Count == 0)
        {
            return 0;
        }

        var documents = await dbContext.KnowledgeDocuments
            .Where(item => ids.Contains(item.KnowledgeDocumentId))
            .ToListAsync(cancellationToken);
        if (documents.Count == 0)
        {
            return 0;
        }

        dbContext.KnowledgeDocuments.RemoveRange(documents);
        await dbContext.SaveChangesAsync(cancellationToken);
        await TryRefreshBm25IndexAsync(cancellationToken);
        return documents.Count;
    }

    public async Task DeleteAsync(int documentId, CancellationToken cancellationToken = default)
    {
        var document = await dbContext.KnowledgeDocuments
            .FirstOrDefaultAsync(item => item.KnowledgeDocumentId == documentId, cancellationToken);
        if (document is null)
        {
            return;
        }

        dbContext.KnowledgeDocuments.Remove(document);
        await dbContext.SaveChangesAsync(cancellationToken);
        await TryRefreshBm25IndexAsync(cancellationToken);
    }

    private async Task TryRefreshBm25IndexAsync(CancellationToken cancellationToken)
    {
        try
        {
            await bm25Index.RebuildAsync(cancellationToken);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            logger.LogWarning(exception, "BM25 index refresh after knowledge document change failed; sparse retrieval will use the previous snapshot.");
        }
    }

    public async Task<KnowledgeDocumentIngestionResult> RebuildEmbeddingsAsync(
        int documentId,
        CancellationToken cancellationToken = default)
    {
        var document = await dbContext.KnowledgeDocuments
            .Include(item => item.Chunks)
            .FirstOrDefaultAsync(item => item.KnowledgeDocumentId == documentId, cancellationToken);
        if (document is null)
        {
            throw new InvalidOperationException("Knowledge document was not found.");
        }

        foreach (var chunk in document.Chunks.OrderBy(item => item.ChunkIndex))
        {
            var embedding = await embeddingService.BuildEmbeddingAsync(BuildEmbeddingText(document, chunk.ChunkText), cancellationToken);
            chunk.Embedding = embedding.Length > 0 ? new Pgvector.Vector(embedding) : null;
            chunk.EmbeddingDimensions = embedding.Length;
        }

        document.Status = "Available";
        document.LastUpdatedTime = DateTimeOffset.UtcNow;
        await dbContext.SaveChangesAsync(cancellationToken);

        return new KnowledgeDocumentIngestionResult(
            document.KnowledgeDocumentId,
            document.Title,
            document.SourceType,
            document.ModuleName,
            document.CharacterCount,
            document.ChunkCount);
    }

    private async Task<KnowledgeDocumentIngestionResult> SaveDocumentAsync(
        string title,
        string sourceType,
        string? fileName,
        string? contentType,
        string text,
        string moduleName,
        string? vendor,
        string? product,
        string? tags,
        bool isMarkdown,
        bool refreshIndex,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            throw new InvalidOperationException("The imported document did not contain extractable text.");
        }

        var chunks = chunkingService.SplitIntoChunks(text, isMarkdown);
        if (chunks.Count == 0)
        {
            throw new InvalidOperationException("The imported document did not produce any chunks.");
        }

        var now = DateTimeOffset.UtcNow;
        var document = new KnowledgeDocument
        {
            Title = Trim(title, 160),
            SourceType = Trim(sourceType, 128),
            FileName = Normalize(fileName, 260),
            ContentType = Normalize(contentType, 120),
            ModuleName = NormalizeModule(moduleName),
            Vendor = Normalize(vendor, 120),
            Product = Normalize(product, 160),
            Tags = Normalize(tags, 800),
            ExtractedText = text,
            ContentHash = BuildContentHash(text),
            CharacterCount = text.Length,
            ChunkCount = chunks.Count,
            CreatedTime = now,
            LastUpdatedTime = now
        };

        for (var index = 0; index < chunks.Count; index++)
        {
            var chunkText = chunks[index];
            var embedding = await embeddingService.BuildEmbeddingAsync(BuildEmbeddingText(document, chunkText), cancellationToken);
            document.Chunks.Add(new KnowledgeDocumentChunk
            {
                ChunkIndex = index,
                ChunkText = Trim(chunkText, 4000),
                Embedding = embedding.Length > 0 ? new Pgvector.Vector(embedding) : null,
                EmbeddingDimensions = embedding.Length,
                CreatedTime = now
            });
        }

        dbContext.KnowledgeDocuments.Add(document);
        await dbContext.SaveChangesAsync(cancellationToken);

        if (refreshIndex)
        {
            await TryRefreshBm25IndexAsync(cancellationToken);
        }

        logger.LogInformation(
            "Imported knowledge document {DocumentId} into module {ModuleName}. Chunks={ChunkCount}.",
            document.KnowledgeDocumentId,
            document.ModuleName,
            document.ChunkCount);

        return new KnowledgeDocumentIngestionResult(
            document.KnowledgeDocumentId,
            document.Title,
            document.SourceType,
            document.ModuleName,
            document.CharacterCount,
            document.ChunkCount);
    }

    private static string BuildEmbeddingText(KnowledgeDocument document, string chunkText)
    {
        var builder = new StringBuilder();
        builder.AppendLine(document.Title);
        builder.AppendLine($"Module: {document.ModuleName}");
        builder.AppendLine($"Vendor: {document.Vendor}");
        builder.AppendLine($"Product: {document.Product}");
        builder.AppendLine($"Tags: {document.Tags}");
        builder.AppendLine(chunkText);
        return builder.ToString();
    }

    private static string NormalizeText(string value)
        => string.Join(Environment.NewLine, value
            .Replace("\r\n", "\n")
            .Replace('\r', '\n')
            .Split('\n', StringSplitOptions.TrimEntries)
            .Where(line => !string.IsNullOrWhiteSpace(line)));

    private static string BuildDocumentText(string? description, string extractedText)
    {
        var normalizedDescription = string.IsNullOrWhiteSpace(description)
            ? null
            : NormalizeText(description);
        return normalizedDescription is null
            ? extractedText
            : $"{normalizedDescription}{Environment.NewLine}{Environment.NewLine}{extractedText}";
    }

    private static string NormalizeModule(string? value)
        => KnowledgeModuleNames.Normalize(value);

    private static string? Normalize(string? value, int maxLength)
        => string.IsNullOrWhiteSpace(value) ? null : Trim(value, maxLength);

    private static string Trim(string value, int maxLength)
    {
        var trimmed = value.Trim();
        return trimmed.Length <= maxLength ? trimmed : trimmed[..maxLength];
    }

    private static string BuildContentHash(string text)
        => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text)));
}
