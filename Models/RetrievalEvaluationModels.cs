using System.ComponentModel.DataAnnotations;

namespace RagAgentConsole.Models;

/// <summary>
/// Persisted, user-editable evaluation case. The golden set ships as seed
/// data but is owned by the knowledge base from then on, so users who swap
/// in their own corpus can define their own cases against it.
/// </summary>
public class RetrievalEvaluationCaseEntity
{
    [Key]
    public int RetrievalEvaluationCaseId { get; set; }

    /// <summary>Stable, human-readable key used to track a case across runs.</summary>
    [MaxLength(96)]
    public required string CaseKey { get; set; }

    [MaxLength(500)]
    public required string Question { get; set; }

    /// <summary>Expected knowledge-document titles, one per line.</summary>
    [MaxLength(2000)]
    public string? ExpectedDocumentTitles { get; set; }

    /// <summary>Expected phrases in a retrieved title or chunk, one per line.</summary>
    [MaxLength(2000)]
    public string? ExpectedContentKeywords { get; set; }

    /// <summary>
    /// Expected domain trace metadata, one key=value pair per line
    /// (e.g. "department=IT" or "documentType=SOP"). A result is relevant
    /// when all pairs match its domain metadata.
    /// </summary>
    [MaxLength(2000)]
    public string? ExpectedMetadata { get; set; }

    [MaxLength(1000)]
    public string? Notes { get; set; }

    /// <summary>True for cases that came from the bundled golden-set seed.</summary>
    public bool IsSeeded { get; set; }

    public DateTimeOffset CreatedTime { get; set; }
    public DateTimeOffset LastUpdatedTime { get; set; }
}

/// <summary>Raw form input for creating or editing an evaluation case.</summary>
public sealed record RetrievalEvaluationCaseDraft(
    string Question,
    string? ExpectedDocumentTitlesText,
    string? Notes,
    string? ExpectedMetadataText = null,
    string? ExpectedContentKeywordsText = null);

/// <summary>
/// A single evaluation case: a query plus the set of identifiers that
/// a retrieval strategy is expected to surface in its top-K results.
/// </summary>
/// <param name="Id">Stable identifier used to track cases across eval runs.</param>
/// <param name="Question">The natural-language query.</param>
/// <param name="ExpectedDocumentTitles">
/// Knowledge document titles that should appear in the top-K.
/// </param>
/// <param name="Notes">Free-form notes explaining the case intent.</param>
/// <param name="ExpectedMetadata">
/// Domain trace metadata pairs a result must all match to count as
/// relevant (e.g. vendor=Citrix). Generic alternative to the id/title
/// label lists for domains with richer metadata.
/// </param>
/// <param name="ExpectedContentKeywords">
/// Phrases that may appear in a retrieved title or chunk. Any matching
/// phrase marks the result as relevant.
/// </param>
public sealed record RetrievalEvaluationCase(
    string Id,
    string Question,
    IReadOnlyList<string> ExpectedDocumentTitles,
    string? Notes = null,
    IReadOnlyDictionary<string, string?>? ExpectedMetadata = null,
    IReadOnlyList<string>? ExpectedContentKeywords = null);

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
