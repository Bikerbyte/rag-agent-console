using System.ComponentModel.DataAnnotations;

namespace CPBLLineBotCloud.Models;

public class SecurityAdvisory
{
    public int SecurityAdvisoryId { get; set; }

    [MaxLength(48)]
    public required string SourceName { get; set; }

    [MaxLength(128)]
    public required string ExternalId { get; set; }

    [MaxLength(32)]
    public string? CveId { get; set; }

    [MaxLength(240)]
    public required string Title { get; set; }

    [MaxLength(4000)]
    public required string Description { get; set; }

    [MaxLength(120)]
    public string? Vendor { get; set; }

    [MaxLength(160)]
    public string? Product { get; set; }

    [MaxLength(32)]
    public string? Severity { get; set; }

    public decimal? CvssScore { get; set; }
    public bool IsKnownExploited { get; set; }
    public bool HasRansomwareUse { get; set; }

    [MaxLength(1200)]
    public string? RequiredAction { get; set; }

    public DateOnly? DueDate { get; set; }
    public DateTimeOffset? PublishedAt { get; set; }
    public DateTimeOffset? LastModifiedAt { get; set; }

    [MaxLength(800)]
    public required string SourceUrl { get; set; }

    [MaxLength(3000)]
    public string? AiSummary { get; set; }

    [MaxLength(1600)]
    public string? SuggestedAction { get; set; }

    [MaxLength(800)]
    public string? Tags { get; set; }

    [MaxLength(128)]
    public required string ContentHash { get; set; }

    public bool IsSent { get; set; }
    public DateTimeOffset CreatedTime { get; set; }
    public DateTimeOffset LastSyncedTime { get; set; }

    public List<SecurityAdvisoryChunk> Chunks { get; set; } = [];
}
