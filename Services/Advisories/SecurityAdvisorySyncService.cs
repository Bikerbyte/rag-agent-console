using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using RagAgentConsole.Data;
using RagAgentConsole.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace RagAgentConsole.Services;

public interface ISecurityAdvisorySyncService
{
    Task<SecurityAdvisorySyncResult> SyncAsync(CancellationToken cancellationToken = default);
}

public class SecurityAdvisorySyncService(
    ApplicationDbContext dbContext,
    IEnumerable<ISecurityAdvisorySource> sources,
    IRagEmbeddingService embeddingService,
    IBm25Index bm25Index,
    IOptions<AppRuntimeOptions> runtimeOptions,
    ILogger<SecurityAdvisorySyncService> logger) : ISecurityAdvisorySyncService
{
    public async Task<SecurityAdvisorySyncResult> SyncAsync(CancellationToken cancellationToken = default)
    {
        var startedAt = DateTimeOffset.UtcNow;
        var sourceList = sources.ToList();
        var fetchedCount = 0;
        var addedCount = 0;
        var updatedCount = 0;
        var chunkCount = 0;

        try
        {
            foreach (var source in sourceList)
            {
                IReadOnlyList<SecurityAdvisoryCandidate> candidates;
                try
                {
                    candidates = await source.FetchLatestAsync(cancellationToken);
                }
                catch (Exception exception)
                {
                    logger.LogWarning(exception, "Sample connector source {SourceName} failed during sync.", source.SourceName);
                    continue;
                }

                fetchedCount += candidates.Count;
                foreach (var candidate in candidates)
                {
                    var contentHash = BuildContentHash(candidate);
                    var existing = await dbContext.SecurityAdvisories
                        .Include(advisory => advisory.Chunks)
                        .FirstOrDefaultAsync(
                            advisory => advisory.SourceName == candidate.SourceName &&
                                        advisory.ExternalId == candidate.ExternalId,
                            cancellationToken);

                    if (existing is null)
                    {
                        var advisory = BuildAdvisory(candidate, contentHash);
                        advisory.Chunks.Add(await BuildChunkAsync(advisory, cancellationToken));
                        dbContext.SecurityAdvisories.Add(advisory);
                        addedCount++;
                        chunkCount++;
                        continue;
                    }

                    existing.LastSyncedTime = DateTimeOffset.UtcNow;
                    if (string.Equals(existing.ContentHash, contentHash, StringComparison.Ordinal))
                    {
                        continue;
                    }

                    UpdateAdvisory(existing, candidate, contentHash);
                    dbContext.SecurityAdvisoryChunks.RemoveRange(existing.Chunks);
                    existing.Chunks.Clear();
                    existing.Chunks.Add(await BuildChunkAsync(existing, cancellationToken));
                    updatedCount++;
                    chunkCount++;
                }
            }

            await dbContext.SaveChangesAsync(cancellationToken);

            if (addedCount > 0 || updatedCount > 0)
            {
                try
                {
                    await bm25Index.RebuildAsync(cancellationToken);
                }
                catch (Exception exception) when (exception is not OperationCanceledException)
                {
                    logger.LogWarning(exception, "BM25 index refresh after sync failed; sparse retrieval will use the previous snapshot.");
                }
            }

            await AddSyncLogAsync(
                startedAt,
                true,
                $"Security advisories synced. Sources: {sourceList.Count}. Fetched: {fetchedCount}. Added: {addedCount}. Updated: {updatedCount}.",
                cancellationToken);

            return new SecurityAdvisorySyncResult(sourceList.Count, fetchedCount, addedCount, updatedCount, chunkCount);
        }
        catch (Exception exception)
        {
            await AddSyncLogAsync(startedAt, false, exception.Message, cancellationToken);
            throw;
        }
    }

    private static SecurityAdvisory BuildAdvisory(SecurityAdvisoryCandidate candidate, string contentHash)
    {
        var now = DateTimeOffset.UtcNow;
        var advisory = new SecurityAdvisory
        {
            SourceName = candidate.SourceName,
            ExternalId = candidate.ExternalId,
            CveId = Normalize(candidate.CveId, 32),
            Title = Trim(candidate.Title, 240),
            Description = Trim(candidate.Description, 4000),
            Vendor = Normalize(candidate.Vendor, 120),
            Product = Normalize(candidate.Product, 160),
            Severity = Normalize(candidate.Severity, 32),
            CvssScore = candidate.CvssScore,
            IsKnownExploited = candidate.IsKnownExploited,
            HasRansomwareUse = candidate.HasRansomwareUse,
            RequiredAction = Normalize(candidate.RequiredAction, 1200),
            DueDate = candidate.DueDate,
            PublishedAt = candidate.PublishedAt,
            LastModifiedAt = candidate.LastModifiedAt,
            SourceUrl = Trim(candidate.SourceUrl, 800),
            ContentHash = contentHash,
            CreatedTime = now,
            LastSyncedTime = now
        };

        advisory.Tags = BuildTags(advisory);
        advisory.AiSummary = BuildLocalSummary(advisory);
        advisory.SuggestedAction = BuildSuggestedAction(advisory);
        return advisory;
    }

    private static void UpdateAdvisory(SecurityAdvisory advisory, SecurityAdvisoryCandidate candidate, string contentHash)
    {
        advisory.CveId = Normalize(candidate.CveId, 32);
        advisory.Title = Trim(candidate.Title, 240);
        advisory.Description = Trim(candidate.Description, 4000);
        advisory.Vendor = Normalize(candidate.Vendor, 120);
        advisory.Product = Normalize(candidate.Product, 160);
        advisory.Severity = Normalize(candidate.Severity, 32);
        advisory.CvssScore = candidate.CvssScore;
        advisory.IsKnownExploited = candidate.IsKnownExploited;
        advisory.HasRansomwareUse = candidate.HasRansomwareUse;
        advisory.RequiredAction = Normalize(candidate.RequiredAction, 1200);
        advisory.DueDate = candidate.DueDate;
        advisory.PublishedAt = candidate.PublishedAt;
        advisory.LastModifiedAt = candidate.LastModifiedAt;
        advisory.SourceUrl = Trim(candidate.SourceUrl, 800);
        advisory.ContentHash = contentHash;
        advisory.LastSyncedTime = DateTimeOffset.UtcNow;
        advisory.IsSent = false;
        advisory.Tags = BuildTags(advisory);
        advisory.AiSummary = BuildLocalSummary(advisory);
        advisory.SuggestedAction = BuildSuggestedAction(advisory);
    }

    private async Task<SecurityAdvisoryChunk> BuildChunkAsync(SecurityAdvisory advisory, CancellationToken cancellationToken)
    {
        var chunkText = BuildChunkText(advisory);
        var embedding = await embeddingService.BuildEmbeddingAsync(chunkText, cancellationToken);

        return new SecurityAdvisoryChunk
        {
            ChunkKind = "AdvisorySummary",
            ChunkText = chunkText,
            Embedding = embedding.Length > 0 ? new Pgvector.Vector(embedding) : null,
            EmbeddingDimensions = embedding.Length,
            CreatedTime = DateTimeOffset.UtcNow
        };
    }

    private static string BuildChunkText(SecurityAdvisory advisory)
    {
        var builder = new StringBuilder();
        builder.AppendLine(advisory.CveId ?? advisory.ExternalId);
        builder.AppendLine(advisory.Title);
        builder.AppendLine(advisory.Description);
        builder.AppendLine($"Source: {advisory.SourceName}");
        builder.AppendLine($"Vendor: {advisory.Vendor}");
        builder.AppendLine($"Product: {advisory.Product}");
        builder.AppendLine($"Severity: {advisory.Severity}");
        builder.AppendLine($"CVSS: {advisory.CvssScore}");
        builder.AppendLine($"Known exploited: {advisory.IsKnownExploited}");
        builder.AppendLine($"Tags: {advisory.Tags}");
        builder.AppendLine($"Required action: {advisory.RequiredAction}");
        return builder.ToString();
    }

    private async Task AddSyncLogAsync(DateTimeOffset startedAt, bool isSuccess, string message, CancellationToken cancellationToken)
    {
        dbContext.SyncJobLogs.Add(new SyncJobLog
        {
            InstanceName = runtimeOptions.Value.InstanceName,
            JobName = "SecurityAdvisorySync",
            StartTime = startedAt,
            EndTime = DateTimeOffset.UtcNow,
            IsSuccess = isSuccess,
            Message = Trim(message, 400)
        });

        await dbContext.SaveChangesAsync(cancellationToken);
    }

    private static string BuildContentHash(SecurityAdvisoryCandidate candidate)
    {
        var content = string.Join('|',
            candidate.SourceName,
            candidate.ExternalId,
            candidate.CveId,
            candidate.Title,
            candidate.Description,
            candidate.Vendor,
            candidate.Product,
            candidate.Severity,
            candidate.CvssScore,
            candidate.IsKnownExploited,
            candidate.HasRansomwareUse,
            candidate.RequiredAction,
            candidate.DueDate,
            candidate.PublishedAt,
            candidate.LastModifiedAt,
            candidate.SourceUrl);

        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(content)));
    }

    private static string BuildLocalSummary(SecurityAdvisory advisory)
    {
        var target = string.Join(" ", new[] { advisory.Vendor, advisory.Product }.Where(value => !string.IsNullOrWhiteSpace(value)));
        var targetText = string.IsNullOrWhiteSpace(target) ? "相關系統" : target;
        var riskText = advisory.IsKnownExploited
            ? "已被列為已知遭利用弱點"
            : advisory.CvssScore >= 9
                ? "屬於高嚴重度弱點"
                : "需要依照環境關聯性評估";

        return $"{advisory.CveId ?? advisory.Title} 影響 {targetText}，{riskText}。{Trim(advisory.Description, 500)}";
    }

    private static string BuildSuggestedAction(SecurityAdvisory advisory)
    {
        if (!string.IsNullOrWhiteSpace(advisory.RequiredAction))
        {
            return advisory.RequiredAction;
        }

        if (advisory.IsKnownExploited || advisory.CvssScore >= 9)
        {
            return "優先確認環境是否使用受影響產品，檢查官方修補版本，並安排緊急更新或暫時緩解措施。";
        }

        return "確認資產是否相關，追蹤廠商公告與修補版本，依內部風險等級安排處理。";
    }

    private static string BuildTags(SecurityAdvisory advisory)
    {
        var tags = new List<string>();
        AddTag(tags, advisory.Vendor);
        AddTag(tags, advisory.Product);
        AddTag(tags, advisory.Severity);
        if (advisory.IsKnownExploited)
        {
            tags.Add("kev");
            tags.Add("exploited");
        }

        if (advisory.HasRansomwareUse)
        {
            tags.Add("ransomware");
        }

        return string.Join(", ", tags.Distinct(StringComparer.OrdinalIgnoreCase));
    }

    private static void AddTag(List<string> tags, string? value)
    {
        if (!string.IsNullOrWhiteSpace(value))
        {
            tags.Add(value.Trim().ToLowerInvariant());
        }
    }

    private static string Trim(string value, int maxLength)
    {
        var compact = string.Join(' ', value.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));
        return compact.Length <= maxLength ? compact : compact[..maxLength];
    }

    private static string? Normalize(string? value, int maxLength)
        => string.IsNullOrWhiteSpace(value) ? null : Trim(value, maxLength);
}
