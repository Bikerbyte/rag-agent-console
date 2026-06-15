using RagAgentConsole.Models;

namespace RagAgentConsole.Services;

public interface IRetrievalTextScorer
{
    double ScoreDocument(RetrievalRequest request, KnowledgeDocument document, string chunkText);
}

/// <summary>
/// Sparse document scorer. BM25 supplies corpus-aware term frequency and
/// length normalization while document metadata contributes searchable text.
/// </summary>
public sealed class RetrievalTextScorer(
    IBm25Index bm25Index,
    ITokenizer tokenizer) : IRetrievalTextScorer
{
    public double ScoreDocument(RetrievalRequest request, KnowledgeDocument document, string chunkText)
    {
        var documentText = string.Join(' ', BuildStructuredDocumentText(document), chunkText);
        if (string.IsNullOrWhiteSpace(documentText))
        {
            return 0;
        }

        var queryTokens = request.Keywords
            .SelectMany(tokenizer.Tokenize)
            .ToList();
        if (queryTokens.Count == 0)
        {
            return 0;
        }

        return bm25Index.Score(queryTokens, tokenizer.Tokenize(documentText));
    }

    internal static string BuildStructuredDocumentText(KnowledgeDocument document)
        => string.Join(' ', document.Title, document.ModuleName, document.SourceType, document.Vendor, document.Product, document.Tags)
            .ToLowerInvariant();
}
