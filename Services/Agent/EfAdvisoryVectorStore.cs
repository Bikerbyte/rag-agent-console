using System.Text.Json;
using SecurityAdvisoryBot.Data;
using SecurityAdvisoryBot.Models;
using Microsoft.EntityFrameworkCore;

namespace SecurityAdvisoryBot.Services;

public class EfAdvisoryVectorStore(
    ApplicationDbContext dbContext,
    IAppSettingsService appSettingsService,
    ILogger<EfAdvisoryVectorStore> logger) : IAdvisoryVectorStore
{
    public async Task<IReadOnlyList<AdvisoryVectorSearchCandidate>> SearchAsync(
        AdvisoryVectorSearchRequest request,
        CancellationToken cancellationToken = default)
    {
        var chunkQuery = dbContext.SecurityAdvisoryChunks
            .Include(chunk => chunk.Advisory)
            .AsQueryable();

        if (!string.IsNullOrWhiteSpace(request.CveId))
        {
            chunkQuery = chunkQuery.Where(chunk =>
                chunk.Advisory != null &&
                (chunk.Advisory.CveId == request.CveId || chunk.Advisory.ExternalId == request.CveId));
        }
        else
        {
            if (request.KevOnly)
            {
                chunkQuery = chunkQuery.Where(chunk => chunk.Advisory != null && chunk.Advisory.IsKnownExploited);
            }

            if (request.HighRiskOnly)
            {
                chunkQuery = chunkQuery.Where(chunk =>
                    chunk.Advisory != null &&
                    (chunk.Advisory.IsKnownExploited ||
                     chunk.Advisory.CvssScore >= 9 ||
                     chunk.Advisory.Severity == "CRITICAL" ||
                     chunk.Advisory.Severity == "Critical"));
            }

            foreach (var keyword in request.Keywords.Take(6))
            {
                var current = keyword;
                chunkQuery = chunkQuery.Where(chunk =>
                    chunk.Advisory != null &&
                    ((chunk.Advisory.CveId != null && chunk.Advisory.CveId.ToLower().Contains(current)) ||
                     (chunk.Advisory.ExternalId != null && chunk.Advisory.ExternalId.ToLower().Contains(current)) ||
                     chunk.Advisory.Title.ToLower().Contains(current) ||
                     (chunk.Advisory.Vendor != null && chunk.Advisory.Vendor.ToLower().Contains(current)) ||
                     (chunk.Advisory.Product != null && chunk.Advisory.Product.ToLower().Contains(current)) ||
                     (chunk.Advisory.Tags != null && chunk.Advisory.Tags.ToLower().Contains(current))));
            }
        }

        var vectorStoreOptions = await appSettingsService.GetVectorStoreOptionsAsync(cancellationToken);
        var chunks = await chunkQuery
            .OrderByDescending(chunk => chunk.SecurityAdvisoryId)
            .Take(Math.Clamp(vectorStoreOptions.CandidateLimit, 50, 10000))
            .ToListAsync(cancellationToken);

        var candidates = new List<AdvisoryVectorSearchCandidate>();
        foreach (var chunk in chunks)
        {
            if (chunk.Advisory is null)
            {
                continue;
            }

            float[]? vector;
            try
            {
                vector = JsonSerializer.Deserialize<float[]>(chunk.EmbeddingJson);
            }
            catch (JsonException exception)
            {
                logger.LogDebug(exception, "Skipping malformed advisory embedding for chunk {ChunkId}.", chunk.SecurityAdvisoryChunkId);
                continue;
            }

            if (vector is null || vector.Length == 0)
            {
                continue;
            }

            var textScore = ScoreTextMatch(request, chunk.Advisory, chunk.ChunkText);
            candidates.Add(new AdvisoryVectorSearchCandidate(chunk.Advisory, chunk.ChunkText, vector, textScore));
        }

        return candidates;
    }

    private static double ScoreTextMatch(
        AdvisoryVectorSearchRequest request,
        SecurityAdvisory advisory,
        string chunkText)
    {
        var score = 0d;
        var structuredSearchable = BuildStructuredSearchableText(advisory);
        var fullSearchable = string.Join(' ', structuredSearchable, advisory.Description, chunkText).ToLowerInvariant();

        if (!string.IsNullOrWhiteSpace(request.CveId) &&
            (string.Equals(advisory.CveId, request.CveId, StringComparison.OrdinalIgnoreCase) ||
             string.Equals(advisory.ExternalId, request.CveId, StringComparison.OrdinalIgnoreCase)))
        {
            score += 4;
        }

        foreach (var keyword in request.Keywords)
        {
            if (structuredSearchable.Contains(keyword, StringComparison.OrdinalIgnoreCase))
            {
                score += 2;
            }
            else if (fullSearchable.Contains(keyword, StringComparison.OrdinalIgnoreCase))
            {
                score += 0.25;
            }
        }

        if (request.KevOnly && advisory.IsKnownExploited)
        {
            score += 1.5;
        }

        if (request.HighRiskOnly &&
            (advisory.IsKnownExploited ||
             advisory.CvssScore >= 9 ||
             string.Equals(advisory.Severity, "Critical", StringComparison.OrdinalIgnoreCase)))
        {
            score += 1.5;
        }

        return score;
    }

    private static string BuildStructuredSearchableText(SecurityAdvisory advisory)
        => string.Join(' ', advisory.CveId, advisory.ExternalId, advisory.Title, advisory.Vendor, advisory.Product, advisory.Tags)
            .ToLowerInvariant();
}
