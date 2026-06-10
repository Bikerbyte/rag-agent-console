namespace RagAgentConsole.Models;

/// <summary>
/// Well-known keys for <see cref="RetrievalPlan"/> entity values shared by
/// multiple domains. Domain-specific keys (e.g. CVE id) are declared next to
/// the domain that interprets them.
/// </summary>
public static class PlanEntityKeys
{
    public const string Vendor = "vendor";
    public const string Product = "product";
    public const string Version = "version";
}

public sealed record RetrievalRequest(
    string Question,
    IReadOnlyList<string> Keywords,
    IReadOnlyDictionary<string, string?> Entities,
    IReadOnlyDictionary<string, string?> Filters,
    float[] QueryEmbedding,
    int MaxResults,
    string? ModuleName = null,
    string RetrievalMode = RetrievalModes.Hybrid,
    DateTimeOffset? PublishedFrom = null,
    DateTimeOffset? PublishedTo = null,
    bool PreferRecent = false)
{
    public string? GetEntity(string key)
        => Entities.TryGetValue(key, out var value) ? value : null;

    public string? GetFilter(string key)
        => Filters.TryGetValue(key, out var value) ? value : null;
}

public abstract record RetrievalCandidate(
    string ChunkText,
    float[] Embedding,
    double TextScore);

public sealed record AdvisoryCandidate(
    SecurityAdvisory Advisory,
    string ChunkText,
    float[] Embedding,
    double TextScore)
    : RetrievalCandidate(ChunkText, Embedding, TextScore);

public sealed record DocumentCandidate(
    KnowledgeDocument Document,
    string ChunkText,
    float[] Embedding,
    double TextScore)
    : RetrievalCandidate(ChunkText, Embedding, TextScore);

public enum PlannerStrategy { Ai, RawFallback }

public sealed record RetrievalPlan(
    string OriginalQuestion,
    string RetrievalQuery,
    string? Intent,
    IReadOnlyList<string> SearchKeywords,
    IReadOnlyList<string> Notes,
    IReadOnlyDictionary<string, string?> Entities,
    IReadOnlyDictionary<string, string?> Filters,
    string ModuleName = KnowledgeModuleNames.CveAdvisory,
    PlannerStrategy Strategy = PlannerStrategy.Ai,
    DateTimeOffset? PublishedFrom = null,
    DateTimeOffset? PublishedTo = null,
    bool PreferRecent = false)
{
    public static readonly IReadOnlyDictionary<string, string?> EmptyValues =
        new Dictionary<string, string?>();

    public string? GetEntity(string key)
        => Entities.TryGetValue(key, out var value) ? value : null;

    public string? GetFilter(string key)
        => Filters.TryGetValue(key, out var value) ? value : null;
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

public sealed record RetrievalResult(
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
    public string SourceName => Advisory?.SourceName ?? Document?.SourceType ?? "Knowledge";
    public string? SourceUrl => Advisory?.SourceUrl;
}

public sealed record RetrievalResponse(
    RetrievalPlan Plan,
    string RetrievalMode,
    IReadOnlyList<RetrievalResult> Results);

public sealed record AgentAnswerResult(
    string Content,
    AgentRetrievalTrace? Trace);

public sealed record AgentRetrievalTrace(
    string OriginalQuestion,
    RetrievalPlan Planner,
    string RetrievalMode,
    IReadOnlyList<AgentRetrievalMatch> Matches);

public sealed record AgentRetrievalMatch(
    int Rank,
    string ModuleName,
    string SourceKind,
    string Title,
    string SourceName,
    double Score,
    double VectorScore,
    double TextScore,
    string ChunkPreview,
    IReadOnlyDictionary<string, string?> Metadata)
{
    public string? GetMetadata(string key)
        => Metadata.TryGetValue(key, out var value) ? value : null;
}
