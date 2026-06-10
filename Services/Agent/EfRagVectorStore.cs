using System.Text.Json;
using RagAgentConsole.Data;
using RagAgentConsole.Models;
using Microsoft.EntityFrameworkCore;

namespace RagAgentConsole.Services;

public class EfRagVectorStore(
    ApplicationDbContext dbContext,
    IAppSettingsService appSettingsService,
    IRetrievalTextScorer scorer,
    ILogger<EfRagVectorStore> logger) : IRagVectorStore
{
    public async Task<IReadOnlyList<RetrievalCandidate>> SearchAsync(
        RetrievalRequest request,
        CancellationToken cancellationToken = default)
    {
        var candidates = new List<RetrievalCandidate>();

        // The advisory corpus is the security advisory domain's storage;
        // its filters arrive as opaque entity/filter values and are parsed
        // through the domain's typed filter view.
        var advisoryFilter = SecurityAdvisoryFilter.From(request);
        var chunkQuery = dbContext.SecurityAdvisoryChunks
            .Include(chunk => chunk.Advisory)
            .AsQueryable();

        if (request.PublishedFrom.HasValue)
        {
            chunkQuery = chunkQuery.Where(chunk =>
                chunk.Advisory != null &&
                chunk.Advisory.PublishedAt >= request.PublishedFrom.Value);
        }

        if (request.PublishedTo.HasValue)
        {
            chunkQuery = chunkQuery.Where(chunk =>
                chunk.Advisory != null &&
                chunk.Advisory.PublishedAt < request.PublishedTo.Value);
        }

        if (advisoryFilter.CveYear.HasValue)
        {
            var cvePrefix = $"CVE-{advisoryFilter.CveYear.Value}-";
            chunkQuery = chunkQuery.Where(chunk =>
                chunk.Advisory != null &&
                chunk.Advisory.CveId != null &&
                chunk.Advisory.CveId.StartsWith(cvePrefix));
        }

        if (!string.IsNullOrWhiteSpace(advisoryFilter.CveId))
        {
            chunkQuery = chunkQuery.Where(chunk =>
                chunk.Advisory != null &&
                (chunk.Advisory.CveId == advisoryFilter.CveId || chunk.Advisory.ExternalId == advisoryFilter.CveId));
        }
        else
        {
            if (advisoryFilter.KevOnly)
            {
                chunkQuery = chunkQuery.Where(chunk => chunk.Advisory != null && chunk.Advisory.IsKnownExploited);
            }

            if (advisoryFilter.HighRiskOnly)
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
            var orderedChunks = request.PreferRecent
                ? chunkQuery.OrderByDescending(chunk => chunk.Advisory!.PublishedAt)
                    .ThenByDescending(chunk => chunk.SecurityAdvisoryId)
                : chunkQuery.OrderByDescending(chunk => chunk.SecurityAdvisoryId);

            var chunks = await orderedChunks
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

                var textScore = scorer.ScoreAdvisory(request, chunk.Advisory, chunk.ChunkText);
                candidates.Add(new AdvisoryCandidate(chunk.Advisory, chunk.ChunkText, vector, textScore));
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

            var textScore = scorer.ScoreDocument(request, chunk.Document, chunk.ChunkText);
            candidates.Add(new DocumentCandidate(chunk.Document, chunk.ChunkText, vector, textScore));
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

    private static bool ShouldSearchAdvisories(RetrievalRequest request)
        => string.IsNullOrWhiteSpace(request.ModuleName) ||
           string.Equals(request.ModuleName, KnowledgeModuleNames.CveAdvisory, StringComparison.OrdinalIgnoreCase);
}
