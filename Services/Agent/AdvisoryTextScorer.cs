using SecurityAdvisoryBot.Models;

namespace SecurityAdvisoryBot.Services;

public sealed class AdvisoryTextScorer : IAdvisoryTextScorer
{
    public double ScoreAdvisory(AdvisoryVectorSearchRequest request, SecurityAdvisory advisory, string chunkText)
    {
        var score = 0d;
        var structuredSearchable = BuildStructuredAdvisoryText(advisory);
        var fullSearchable = string.Join(' ', structuredSearchable, advisory.Description, chunkText).ToLowerInvariant();

        if (!string.IsNullOrWhiteSpace(request.CveId) &&
            (string.Equals(advisory.CveId, request.CveId, StringComparison.OrdinalIgnoreCase) ||
             string.Equals(advisory.ExternalId, request.CveId, StringComparison.OrdinalIgnoreCase)))
        {
            score += 4;
        }

        foreach (var keyword in request.Keywords)
        {
            if (structuredSearchable.Contains(keyword, StringComparison.OrdinalIgnoreCase))
            {
                score += 2;
            }
            else if (fullSearchable.Contains(keyword, StringComparison.OrdinalIgnoreCase))
            {
                score += 0.25;
            }
        }

        if (request.KevOnly && advisory.IsKnownExploited)
        {
            score += 1.5;
        }

        if (request.HighRiskOnly &&
            (advisory.IsKnownExploited ||
             advisory.CvssScore >= 9 ||
             string.Equals(advisory.Severity, "Critical", StringComparison.OrdinalIgnoreCase)))
        {
            score += 1.5;
        }

        return score;
    }

    public double ScoreDocument(AdvisoryVectorSearchRequest request, KnowledgeDocument document, string chunkText)
    {
        var score = 0d;
        var structuredSearchable = string.Join(' ', document.Title, document.ModuleName, document.SourceType, document.Vendor, document.Product, document.Tags)
            .ToLowerInvariant();
        var fullSearchable = string.Join(' ', structuredSearchable, chunkText).ToLowerInvariant();

        foreach (var keyword in request.Keywords)
        {
            if (structuredSearchable.Contains(keyword, StringComparison.OrdinalIgnoreCase))
            {
                score += 2;
            }
            else if (fullSearchable.Contains(keyword, StringComparison.OrdinalIgnoreCase))
            {
                score += 0.25;
            }
        }

        return score;
    }

    private static string BuildStructuredAdvisoryText(SecurityAdvisory advisory)
        => string.Join(' ', advisory.CveId, advisory.ExternalId, advisory.Title, advisory.Vendor, advisory.Product, advisory.Tags)
            .ToLowerInvariant();
}
