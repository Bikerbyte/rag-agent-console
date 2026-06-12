using RagAgentConsole.Data;
using RagAgentConsole.Models;
using Microsoft.EntityFrameworkCore;
using Pgvector;
using Pgvector.EntityFrameworkCore;

namespace RagAgentConsole.Services;

public interface IRagVectorStore
{
    Task<IReadOnlyList<RetrievalCandidate>> SearchAsync(
        RetrievalRequest request,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// 唯一的向量檢索後端：結構化條件（日期、CVE 年份、模組、啟用狀態、維度）走 SQL WHERE，
/// 相似度排序靠 pgvector 的 cosine distance，關鍵字與領域規則留在記憶體做後過濾。
/// </summary>
public class PgVectorRagVectorStore(
    ApplicationDbContext dbContext,
    IAppSettingsService appSettingsService,
    IRetrievalTextScorer scorer,
    IRagDomainRegistry domainRegistry,
    ILogger<PgVectorRagVectorStore> logger) : IRagVectorStore
{
    public async Task<IReadOnlyList<RetrievalCandidate>> SearchAsync(
        RetrievalRequest request,
        CancellationToken cancellationToken = default)
    {
        var vectorStoreOptions = await appSettingsService.GetVectorStoreOptionsAsync(cancellationToken);
        var limit = Math.Clamp(vectorStoreOptions.CandidateLimit, 50, 10000);
        var queryVector = request.QueryEmbedding.Length > 0 ? new Vector(request.QueryEmbedding) : null;
        var candidates = new List<RetrievalCandidate>();

        if (ShouldSearchAdvisories(request))
        {
            candidates.AddRange(await SearchAdvisoriesAsync(request, queryVector, limit, cancellationToken));
        }

        candidates.AddRange(await SearchDocumentsAsync(request, queryVector, limit, cancellationToken));

        logger.LogDebug("PgVector search produced {CandidateCount} candidates.", candidates.Count);
        return candidates;
    }

    private async Task<List<RetrievalCandidate>> SearchAdvisoriesAsync(
        RetrievalRequest request,
        Vector? queryVector,
        int limit,
        CancellationToken cancellationToken)
    {
        var advisoryFilter = SecurityAdvisoryFilter.From(request);
        var query = dbContext.SecurityAdvisoryChunks
            .Include(chunk => chunk.Advisory)
            .Where(chunk => chunk.Advisory != null)
            .AsQueryable();

        if (request.PublishedFrom.HasValue)
        {
            query = query.Where(chunk => chunk.Advisory!.PublishedAt >= request.PublishedFrom.Value);
        }

        if (request.PublishedTo.HasValue)
        {
            query = query.Where(chunk => chunk.Advisory!.PublishedAt < request.PublishedTo.Value);
        }

        if (advisoryFilter.CveYear.HasValue)
        {
            var cvePrefix = $"CVE-{advisoryFilter.CveYear.Value}-";
            query = query.Where(chunk => chunk.Advisory!.CveId != null && chunk.Advisory.CveId.StartsWith(cvePrefix));
        }

        if (!string.IsNullOrWhiteSpace(advisoryFilter.CveId))
        {
            query = query.Where(chunk =>
                chunk.Advisory!.CveId == advisoryFilter.CveId || chunk.Advisory.ExternalId == advisoryFilter.CveId);
        }
        else
        {
            if (advisoryFilter.KevOnly)
            {
                query = query.Where(chunk => chunk.Advisory!.IsKnownExploited);
            }

            if (advisoryFilter.HighRiskOnly)
            {
                query = query.Where(chunk =>
                    chunk.Advisory!.IsKnownExploited ||
                    chunk.Advisory.CvssScore >= 9 ||
                    chunk.Advisory.Severity == "CRITICAL" ||
                    chunk.Advisory.Severity == "Critical");
            }
        }

        // 有 query embedding 時依 cosine distance 排序；沒有（純關鍵字模式）就退回時間序。
        // 維度過濾擋掉換 embedding provider 之後遺留的不相容向量。
        if (queryVector is not null)
        {
            var dimensions = queryVector.ToArray().Length;
            query = query
                .Where(chunk => chunk.Embedding != null && chunk.EmbeddingDimensions == dimensions)
                .OrderBy(chunk => chunk.Embedding!.CosineDistance(queryVector));
        }
        else
        {
            query = query
                .Where(chunk => chunk.Embedding != null)
                .OrderByDescending(chunk => chunk.Advisory!.PublishedAt);
        }

        var chunks = await query
            .Take(limit)
            .AsNoTracking()
            .ToListAsync(cancellationToken);

        var results = new List<RetrievalCandidate>();
        foreach (var chunk in chunks)
        {
            if (chunk.Advisory is null ||
                chunk.Embedding is null ||
                !MatchesAdvisoryKeywords(request, chunk.Advisory))
            {
                continue;
            }

            results.Add(new AdvisoryCandidate(
                chunk.Advisory,
                chunk.ChunkText,
                chunk.Embedding.ToArray(),
                scorer.ScoreAdvisory(request, chunk.Advisory, chunk.ChunkText)));
        }

        return results;
    }

    private async Task<List<RetrievalCandidate>> SearchDocumentsAsync(
        RetrievalRequest request,
        Vector? queryVector,
        int limit,
        CancellationToken cancellationToken)
    {
        var documentDomain = domainRegistry.Resolve(request.ModuleName);
        var query = dbContext.KnowledgeDocumentChunks
            .Include(chunk => chunk.Document)
            .Where(chunk => chunk.Document != null && chunk.Document.IsEnabled);

        if (!string.IsNullOrWhiteSpace(request.ModuleName))
        {
            var moduleNames = documentDomain.ModuleNames;
            query = query.Where(chunk => moduleNames.Contains(chunk.Document!.ModuleName));
        }

        if (queryVector is not null)
        {
            var dimensions = queryVector.ToArray().Length;
            query = query
                .Where(chunk => chunk.Embedding != null && chunk.EmbeddingDimensions == dimensions)
                .OrderBy(chunk => chunk.Embedding!.CosineDistance(queryVector));
        }
        else
        {
            query = query
                .Where(chunk => chunk.Embedding != null)
                .OrderByDescending(chunk => chunk.KnowledgeDocumentChunkId);
        }

        var chunks = await query
            .Take(limit)
            .AsNoTracking()
            .ToListAsync(cancellationToken);

        var results = new List<RetrievalCandidate>();
        foreach (var chunk in chunks)
        {
            if (chunk.Document is null ||
                chunk.Embedding is null ||
                !MatchesDocumentKeywords(request, chunk.Document, chunk.ChunkText) ||
                !documentDomain.AcceptsDocument(request, chunk.Document, chunk.ChunkText))
            {
                continue;
            }

            results.Add(new DocumentCandidate(
                chunk.Document,
                chunk.ChunkText,
                chunk.Embedding.ToArray(),
                scorer.ScoreDocument(request, chunk.Document, chunk.ChunkText)));
        }

        return results;
    }

    private static bool ShouldSearchAdvisories(RetrievalRequest request)
        => string.IsNullOrWhiteSpace(request.ModuleName) ||
           string.Equals(request.ModuleName, KnowledgeModuleNames.CveAdvisory, StringComparison.OrdinalIgnoreCase);

    private static bool MatchesAdvisoryKeywords(RetrievalRequest request, SecurityAdvisory advisory)
    {
        if (request.Keywords.Count == 0)
        {
            return true;
        }

        var searchable = RetrievalTextScorer.BuildStructuredAdvisoryText(advisory);
        return request.Keywords.Take(6).Any(keyword => searchable.Contains(keyword, StringComparison.OrdinalIgnoreCase));
    }

    private static bool MatchesDocumentKeywords(RetrievalRequest request, KnowledgeDocument document, string chunkText)
    {
        if (request.Keywords.Count == 0)
        {
            return true;
        }

        var searchable = RetrievalTextScorer.BuildStructuredDocumentText(document) + " " + chunkText;
        return request.Keywords.Take(6).Any(keyword => searchable.Contains(keyword, StringComparison.OrdinalIgnoreCase));
    }
}
