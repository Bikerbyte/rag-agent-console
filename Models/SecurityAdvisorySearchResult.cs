namespace CPBLLineBotCloud.Models;

public sealed record SecurityAdvisorySearchResult(
    SecurityAdvisory Advisory,
    string ChunkText,
    double Score);

public sealed record SecurityAdvisorySyncResult(
    int SourceCount,
    int FetchedCount,
    int AddedCount,
    int UpdatedCount,
    int ChunkCount);
