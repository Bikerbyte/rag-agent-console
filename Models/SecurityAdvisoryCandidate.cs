namespace SecurityAdvisoryBot.Models;

public sealed record SecurityAdvisoryCandidate(
    string SourceName,
    string ExternalId,
    string? CveId,
    string Title,
    string Description,
    string? Vendor,
    string? Product,
    string? Severity,
    decimal? CvssScore,
    bool IsKnownExploited,
    bool HasRansomwareUse,
    string? RequiredAction,
    DateOnly? DueDate,
    DateTimeOffset? PublishedAt,
    DateTimeOffset? LastModifiedAt,
    string SourceUrl);
