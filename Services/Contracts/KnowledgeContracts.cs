using SecurityAdvisoryBot.Models;

namespace SecurityAdvisoryBot.Services;

public interface IKnowledgeDocumentTextExtractor
{
    Task<KnowledgeExtractedText> ExtractAsync(
        string fileName,
        string? contentType,
        Stream contentStream,
        CancellationToken cancellationToken = default);
}

public interface IKnowledgeTextChunkingService
{
    IReadOnlyList<string> SplitIntoChunks(string text, bool isMarkdown = false);
}

public interface IKnowledgeDocumentIngestionService
{
    Task<KnowledgeDocumentIngestionResult> ImportTextAsync(
        KnowledgeDocumentImportRequest request,
        CancellationToken cancellationToken = default);

    Task<KnowledgeDocumentIngestionResult> ImportFileAsync(
        KnowledgeDocumentFileImportRequest request,
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
    string? Tags);
