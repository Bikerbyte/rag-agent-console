namespace SecurityAdvisoryBot.Models;

public sealed record AdvisoryVectorSearchRequest(
    string Question,
    string? CveId,
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
