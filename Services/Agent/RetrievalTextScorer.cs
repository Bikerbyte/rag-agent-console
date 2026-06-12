using RagAgentConsole.Models;

namespace RagAgentConsole.Services;

public interface IRetrievalTextScorer
{
    double ScoreAdvisory(RetrievalRequest request, SecurityAdvisory advisory, string chunkText);
    double ScoreDocument(RetrievalRequest request, KnowledgeDocument document, string chunkText);
}

/// <summary>
/// Sparse text scorer used in hybrid retrieval. BM25 provides the bulk
/// of the signal (corpus-aware TF-IDF with length normalization);
/// small structural bonuses are layered on top for hard intent matches
/// (CVE ID equality, KEV alignment, high-risk filter).
/// </summary>
public sealed class RetrievalTextScorer(
    IBm25Index bm25Index,
    ITokenizer tokenizer) : IRetrievalTextScorer
{
    private const double CveExactMatchBonus = 4.0;
    private const double KevAlignmentBonus = 1.5;
    private const double HighRiskAlignmentBonus = 1.5;

    public double ScoreAdvisory(RetrievalRequest request, SecurityAdvisory advisory, string chunkText)
    {
        var documentText = string.Join(
            ' ',
            BuildStructuredAdvisoryText(advisory),
            advisory.Description,
            chunkText);

        var advisoryFilter = SecurityAdvisoryFilter.From(request);
        var bm25 = ScoreBm25(request, documentText);
        var score = bm25;

        if (!string.IsNullOrWhiteSpace(advisoryFilter.CveId) &&
            (string.Equals(advisory.CveId, advisoryFilter.CveId, StringComparison.OrdinalIgnoreCase) ||
             string.Equals(advisory.ExternalId, advisoryFilter.CveId, StringComparison.OrdinalIgnoreCase)))
        {
            score += CveExactMatchBonus;
        }

        if (advisoryFilter.KevOnly && advisory.IsKnownExploited)
        {
            score += KevAlignmentBonus;
        }

        if (advisoryFilter.HighRiskOnly &&
            (advisory.IsKnownExploited ||
             advisory.CvssScore >= 9 ||
             string.Equals(advisory.Severity, "Critical", StringComparison.OrdinalIgnoreCase)))
        {
            score += HighRiskAlignmentBonus;
        }

        return score;
    }

    public double ScoreDocument(RetrievalRequest request, KnowledgeDocument document, string chunkText)
    {
        var documentText = string.Join(
            ' ',
            BuildStructuredDocumentText(document),
            chunkText);

        return ScoreBm25(request, documentText);
    }

    private double ScoreBm25(RetrievalRequest request, string documentText)
    {
        if (string.IsNullOrWhiteSpace(documentText))
        {
            return 0;
        }

        // Query side: planner keywords drive intent; a CVE id entity is added
        // as a strong sparse signal even when keyword extraction missed it.
        var queryTokens = new List<string>();
        foreach (var keyword in request.Keywords)
        {
            queryTokens.AddRange(tokenizer.Tokenize(keyword));
        }

        var cveId = request.GetEntity(SecurityAdvisoryPlanKeys.CveId);
        if (!string.IsNullOrWhiteSpace(cveId))
        {
            queryTokens.AddRange(tokenizer.Tokenize(cveId));
        }

        if (queryTokens.Count == 0)
        {
            return 0;
        }

        var documentTokens = tokenizer.Tokenize(documentText);
        return bm25Index.Score(queryTokens, documentTokens);
    }

    internal static string BuildStructuredAdvisoryText(SecurityAdvisory advisory)
        => string.Join(' ', advisory.CveId, advisory.ExternalId, advisory.Title, advisory.Vendor, advisory.Product, advisory.Tags)
            .ToLowerInvariant();

    internal static string BuildStructuredDocumentText(KnowledgeDocument document)
        => string.Join(' ', document.Title, document.ModuleName, document.SourceType, document.Vendor, document.Product, document.Tags)
            .ToLowerInvariant();
}
