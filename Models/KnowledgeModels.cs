using System.ComponentModel.DataAnnotations;

namespace RagAgentConsole.Models;

public static class KnowledgeModuleNames
{
    public const string WorkflowQa = "WorkflowQa";
    public const string InternalDocs = "InternalDocs";

    public static string Normalize(string? value)
    {
        if (string.Equals(value, WorkflowQa, StringComparison.OrdinalIgnoreCase))
        {
            return WorkflowQa;
        }

        return InternalDocs;
    }
}

public class KnowledgeDocument
{
    public int KnowledgeDocumentId { get; set; }

    [MaxLength(64)]
    public string ModuleName { get; set; } = KnowledgeModuleNames.InternalDocs;

    [MaxLength(160)]
    public required string Title { get; set; }

    [MaxLength(128)]
    public required string SourceType { get; set; }

    [MaxLength(260)]
    public string? FileName { get; set; }

    [MaxLength(120)]
    public string? ContentType { get; set; }

    [MaxLength(120)]
    public string? Vendor { get; set; }

    [MaxLength(160)]
    public string? Product { get; set; }

    [MaxLength(800)]
    public string? Tags { get; set; }

    public required string ExtractedText { get; set; }

    [MaxLength(128)]
    public required string ContentHash { get; set; }

    [MaxLength(32)]
    public string Status { get; set; } = "Available";

    public bool IsEnabled { get; set; } = true;
    public int CharacterCount { get; set; }
    public int ChunkCount { get; set; }
    public DateTimeOffset CreatedTime { get; set; }
    public DateTimeOffset LastUpdatedTime { get; set; }

    public List<KnowledgeDocumentChunk> Chunks { get; set; } = [];
}

public class KnowledgeDocumentChunk
{
    public int KnowledgeDocumentChunkId { get; set; }
    public int KnowledgeDocumentId { get; set; }

    [MaxLength(32)]
    public string ChunkKind { get; set; } = "DocumentText";

    public int ChunkIndex { get; set; }

    [MaxLength(4000)]
    public required string ChunkText { get; set; }

    /// <summary>pgvector 欄位。維度由 embedding provider 決定，查詢時用 <see cref="EmbeddingDimensions"/> 過濾不相容的舊資料。</summary>
    public Pgvector.Vector? Embedding { get; set; }

    public int EmbeddingDimensions { get; set; }
    public DateTimeOffset CreatedTime { get; set; }

    public KnowledgeDocument? Document { get; set; }
}

public sealed record KnowledgeDocumentIngestionResult(
    int KnowledgeDocumentId,
    string Title,
    string SourceType,
    string ModuleName,
    int CharacterCount,
    int ChunkCount);

public sealed record KnowledgeExtractedText(
    string Text,
    string ParserName,
    string NormalizedContentType);
