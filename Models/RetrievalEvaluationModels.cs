namespace SecurityAdvisoryBot.Models;

/// <summary>
/// A single evaluation case: a query plus the set of identifiers that
/// a retrieval strategy is expected to surface in its top-K results.
/// </summary>
/// <param name="Id">Stable identifier used to track cases across eval runs.</param>
/// <param name="Question">The natural-language query.</param>
/// <param name="ExpectedCveIds">
/// CVE IDs that should appear in the top-K. A case passes Hit@K if any
/// of these IDs appears in the first K results.
/// </param>
/// <param name="ExpectedDocumentTitles">
/// Knowledge document titles that should appear in the top-K, used for
/// non-CVE knowledge questions.
/// </param>
/// <param name="Notes">Free-form notes explaining the case intent.</param>
public sealed record RetrievalEvaluationCase(
    string Id,
    string Question,
    IReadOnlyList<string> ExpectedCveIds,
    IReadOnlyList<string> ExpectedDocumentTitles,
    string? Notes = null);

/// <summary>The outcome of running a single case through a retrieval strategy.</summary>
public sealed record RetrievalEvaluationCaseResult(
    RetrievalEvaluationCase Case,
    string RetrievalMode,
    bool HitAt1,
    bool HitAt5,
    double ReciprocalRank,
    int? FirstRelevantRank,
    IReadOnlyList<RetrievalEvaluationMatch> TopResults);

public sealed record RetrievalEvaluationMatch(
    int Rank,
    string Title,
    string? CveId,
    double Score,
    double VectorScore,
    double TextScore,
    bool IsRelevant);

/// <summary>Aggregated metrics across all cases for one retrieval strategy.</summary>
public sealed record RetrievalEvaluationSummary(
    string RetrievalMode,
    int CaseCount,
    double HitAt1,
    double HitAt5,
    double MeanReciprocalRank,
    IReadOnlyList<RetrievalEvaluationCaseResult> CaseResults);

/// <summary>Result of a full evaluation run across one or more strategies.</summary>
public sealed record RetrievalEvaluationReport(
    DateTimeOffset RanAt,
    IReadOnlyList<RetrievalEvaluationSummary> Summaries);
