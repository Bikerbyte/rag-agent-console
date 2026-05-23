namespace SecurityAdvisoryBot.Models;

public sealed record AdvisoryVectorSearchRequest(
    string Question,
    string? CveId,
    string? Version,
    bool KevOnly,
    bool HighRiskOnly,
    IReadOnlyList<string> Keywords,
    float[] QueryEmbedding,
    int MaxResults);

public sealed record AdvisoryVectorSearchCandidate(
    SecurityAdvisory Advisory,
    string ChunkText,
    float[] Embedding,
    double TextScore);

public sealed record AdvisoryQueryPlan(
    string OriginalQuestion,
    string RetrievalQuery,
    string? Intent,
    string? Vendor,
    string? Product,
    string? Version,
    string? CveId,
    string? RiskFilter,
    IReadOnlyList<string> SearchKeywords,
    IReadOnlyList<string> Notes)
{
    public bool KevOnly => string.Equals(RiskFilter, "known_exploited", StringComparison.OrdinalIgnoreCase);

    public bool HighRiskOnly =>
        string.Equals(RiskFilter, "critical", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(RiskFilter, "high_risk", StringComparison.OrdinalIgnoreCase);
}
