namespace RagAgentConsole.Models;

public sealed record RetrievalRequest(
    string Question,
    IReadOnlyList<string> Keywords,
    IReadOnlyDictionary<string, string?> Filters,
    float[] QueryEmbedding,
    int MaxResults,
    string? ModuleName = null,
    string RetrievalMode = RetrievalModes.Hybrid)
{
    public string? GetFilter(string key)
        => Filters.TryGetValue(key, out var value) ? value : null;
}

public sealed record RetrievalCandidate(
    KnowledgeDocument Document,
    string ChunkText,
    float[] Embedding,
    double TextScore);

public enum PlannerStrategy { Ai, RawFallback }

public sealed record RetrievalPlan(
    string OriginalQuestion,
    string RetrievalQuery,
    string? Intent,
    IReadOnlyList<string> SearchKeywords,
    IReadOnlyList<string> Notes,
    IReadOnlyDictionary<string, string?> Entities,
    IReadOnlyDictionary<string, string?> Filters,
    string ModuleName = KnowledgeModuleNames.InternalDocs,
    PlannerStrategy Strategy = PlannerStrategy.Ai)
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
    KnowledgeDocument Document,
    string ChunkText,
    double Score,
    double VectorScore,
    double TextScore)
{
    public string ModuleName => Document.ModuleName;
    public string SourceKind => "ManagedDocument";
    public string Title => Document.Title;
    public string SourceName => Document.SourceType;

    public IReadOnlyDictionary<string, string?> BuildMetadata()
    {
        var metadata = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase)
        {
            ["moduleName"] = Document.ModuleName,
            ["sourceType"] = Document.SourceType
        };

        AddIfPresent(metadata, "vendor", Document.Vendor);
        AddIfPresent(metadata, "product", Document.Product);
        AddIfPresent(metadata, "tags", Document.Tags);
        return metadata;
    }

    private static void AddIfPresent(Dictionary<string, string?> metadata, string key, string? value)
    {
        if (!string.IsNullOrWhiteSpace(value))
        {
            metadata[key] = value;
        }
    }
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
