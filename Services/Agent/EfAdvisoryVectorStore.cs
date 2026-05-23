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
        var candidates = new List<AdvisoryVectorSearchCandidate>();
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

            var searchableKeywords = request.Keywords.Take(6).ToList();
            if (searchableKeywords.Count > 0)
            {
                var primaryKeyword = searchableKeywords[0];
                chunkQuery = chunkQuery.Where(chunk =>
                    chunk.Advisory != null &&
                    ((chunk.Advisory.CveId != null && chunk.Advisory.CveId.ToLower().Contains(primaryKeyword)) ||
                     (chunk.Advisory.ExternalId != null && chunk.Advisory.ExternalId.ToLower().Contains(primaryKeyword)) ||
                     chunk.Advisory.Title.ToLower().Contains(primaryKeyword) ||
                     (chunk.Advisory.Vendor != null && chunk.Advisory.Vendor.ToLower().Contains(primaryKeyword)) ||
                     (chunk.Advisory.Product != null && chunk.Advisory.Product.ToLower().Contains(primaryKeyword)) ||
                     (chunk.Advisory.Tags != null && chunk.Advisory.Tags.ToLower().Contains(primaryKeyword))));
            }
        }

        var vectorStoreOptions = await appSettingsService.GetVectorStoreOptionsAsync(cancellationToken);
        var limit = Math.Clamp(vectorStoreOptions.CandidateLimit, 50, 10000);

        if (ShouldSearchAdvisories(request))
        {
            var chunks = await chunkQuery
                .OrderByDescending(chunk => chunk.SecurityAdvisoryId)
                .Take(limit)
                .ToListAsync(cancellationToken);

            foreach (var chunk in chunks)
            {
                if (chunk.Advisory is null)
                {
                    continue;
                }

                var vector = TryReadVector(chunk.EmbeddingJson, "advisory", chunk.SecurityAdvisoryChunkId);
                if (vector is null)
                {
                    continue;
                }

                var textScore = ScoreAdvisoryTextMatch(request, chunk.Advisory, chunk.ChunkText);
                candidates.Add(new AdvisoryVectorSearchCandidate(chunk.Advisory, chunk.ChunkText, vector, textScore));
            }
        }

        var documentQuery = dbContext.KnowledgeDocumentChunks
            .Include(chunk => chunk.Document)
            .Where(chunk => chunk.Document != null && chunk.Document.IsEnabled)
            .AsQueryable();

        if (!string.IsNullOrWhiteSpace(request.ModuleName))
        {
            documentQuery = documentQuery.Where(chunk => chunk.Document != null && chunk.Document.ModuleName == request.ModuleName);
        }

        var documentKeywords = request.Keywords.Take(6).ToList();
        if (documentKeywords.Count > 0)
        {
            var primaryKeyword = documentKeywords[0];
            documentQuery = documentQuery.Where(chunk =>
                chunk.Document != null &&
                (chunk.Document.Title.ToLower().Contains(primaryKeyword) ||
                 (chunk.Document.Vendor != null && chunk.Document.Vendor.ToLower().Contains(primaryKeyword)) ||
                 (chunk.Document.Product != null && chunk.Document.Product.ToLower().Contains(primaryKeyword)) ||
                 (chunk.Document.Tags != null && chunk.Document.Tags.ToLower().Contains(primaryKeyword)) ||
                 chunk.ChunkText.ToLower().Contains(primaryKeyword)));
        }

        var documentChunks = await documentQuery
            .OrderByDescending(chunk => chunk.KnowledgeDocumentChunkId)
            .Take(limit)
            .ToListAsync(cancellationToken);

        foreach (var chunk in documentChunks)
        {
            if (chunk.Document is null)
            {
                continue;
            }

            var vector = TryReadVector(chunk.EmbeddingJson, "knowledge document", chunk.KnowledgeDocumentChunkId);
            if (vector is null)
            {
                continue;
            }

            var textScore = ScoreDocumentTextMatch(request, chunk.Document, chunk.ChunkText);
            candidates.Add(new AdvisoryVectorSearchCandidate(chunk.Document, chunk.ChunkText, vector, textScore));
        }

        return candidates;
    }

    private float[]? TryReadVector(string embeddingJson, string sourceKind, int chunkId)
    {
        try
        {
            var vector = JsonSerializer.Deserialize<float[]>(embeddingJson);
            return vector is { Length: > 0 } ? vector : null;
        }
        catch (JsonException exception)
        {
            logger.LogDebug(exception, "Skipping malformed {SourceKind} embedding for chunk {ChunkId}.", sourceKind, chunkId);
            return null;
        }
    }

    private static bool ShouldSearchAdvisories(AdvisoryVectorSearchRequest request)
        => string.IsNullOrWhiteSpace(request.ModuleName) ||
           string.Equals(request.ModuleName, KnowledgeModuleNames.CveAdvisory, StringComparison.OrdinalIgnoreCase);

    private static double ScoreAdvisoryTextMatch(
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

    private static double ScoreDocumentTextMatch(
        AdvisoryVectorSearchRequest request,
        KnowledgeDocument document,
        string chunkText)
    {
        var score = 0d;
        var structuredSearchable = string.Join(' ', document.Title, document.ModuleName, document.SourceType, document.Vendor, document.Product, document.Tags)
            .ToLowerInvariant();
        var fullSearchable = string.Join(' ', structuredSearchable, chunkText).ToLowerInvariant();

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

        return score;
    }

    private static string BuildStructuredSearchableText(SecurityAdvisory advisory)
        => string.Join(' ', advisory.CveId, advisory.ExternalId, advisory.Title, advisory.Vendor, advisory.Product, advisory.Tags)
            .ToLowerInvariant();
}
