namespace RagAgentConsole.Models;

public sealed record AdvisoryVectorSearchRequest(
    string Question,
    string? CveId,
    string? Version,
    bool KevOnly,
    bool HighRiskOnly,
    IReadOnlyList<string> Keywords,
    float[] QueryEmbedding,
    int MaxResults,
    string? ModuleName = null,
    string RetrievalMode = RetrievalModes.Hybrid,
    DateTimeOffset? PublishedFrom = null,
    DateTimeOffset? PublishedTo = null,
    bool PreferRecent = false,
    int? CveYear = null);

public abstract record AdvisoryVectorSearchCandidate(
    string ChunkText,
    float[] Embedding,
    double TextScore);

public sealed record AdvisoryCandidate(
    SecurityAdvisory Advisory,
    string ChunkText,
    float[] Embedding,
    double TextScore)
    : AdvisoryVectorSearchCandidate(ChunkText, Embedding, TextScore);

public sealed record DocumentCandidate(
    KnowledgeDocument Document,
    string ChunkText,
    float[] Embedding,
    double TextScore)
    : AdvisoryVectorSearchCandidate(ChunkText, Embedding, TextScore);

public enum PlannerStrategy { Ai, RawFallback }

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
    IReadOnlyList<string> Notes,
    string ModuleName = KnowledgeModuleNames.CveAdvisory,
    PlannerStrategy Strategy = PlannerStrategy.Ai,
    DateTimeOffset? PublishedFrom = null,
    DateTimeOffset? PublishedTo = null,
    bool PreferRecent = false,
    int? CveYear = null)
{
    public bool KevOnly => string.Equals(RiskFilter, "known_exploited", StringComparison.OrdinalIgnoreCase);

    public bool HighRiskOnly =>
        string.Equals(RiskFilter, "critical", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(RiskFilter, "high_risk", StringComparison.OrdinalIgnoreCase);
}

public static class RetrievalModes
{
    public const string Hybrid = "Hybrid";
    public const string Vector = "Vector";
    public const string Keyword = "Keyword";

    public static string Normalize(string? value)
        => value switch
        {
            Vector => Vector,
            Keyword => Keyword,
            _ => Hybrid
        };
}

public sealed record SecurityAdvisorySearchResult(
    SecurityAdvisory? Advisory,
    KnowledgeDocument? Document,
    string ChunkText,
    double Score,
    double VectorScore,
    double TextScore)
{
    public string ModuleName => Document?.ModuleName ?? KnowledgeModuleNames.CveAdvisory;
    public string SourceKind => Advisory is not null ? "OfficialAdvisory" : "ManagedDocument";
    public string Title => Advisory?.Title ?? Document?.Title ?? "Untitled";
    public string? CveId => Advisory?.CveId ?? Advisory?.ExternalId;
    public string? Vendor => Advisory?.Vendor ?? Document?.Vendor;
    public string? Product => Advisory?.Product ?? Document?.Product;
    public string? Severity => Advisory?.Severity;
    public decimal? CvssScore => Advisory?.CvssScore;
    public bool IsKnownExploited => Advisory?.IsKnownExploited == true;
    public string SourceName => Advisory?.SourceName ?? Document?.SourceType ?? "Knowledge";
    public string? SourceUrl => Advisory?.SourceUrl;
}

public sealed record SecurityAdvisorySearchResponse(
    AdvisoryQueryPlan Plan,
    string RetrievalMode,
    IReadOnlyList<SecurityAdvisorySearchResult> Results);

public sealed record AgentAnswerResult(
    string Content,
    AgentRetrievalTrace? Trace);

public sealed record AgentRetrievalTrace(
    string OriginalQuestion,
    AdvisoryQueryPlan Planner,
    string RetrievalMode,
    IReadOnlyList<AgentRetrievalMatch> Matches);

public sealed record AgentRetrievalMatch(
    int Rank,
    string ModuleName,
    string SourceKind,
    string Title,
    string? CveId,
    string? Vendor,
    string? Product,
    string? Severity,
    bool IsKnownExploited,
    string SourceName,
    double Score,
    double VectorScore,
    double TextScore,
    string ChunkPreview);
