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
/// Document-only pgvector store. SQL handles module, enabled-state, embedding
/// dimensions, and cosine ordering; generic metadata filters are applied to
/// the bounded candidate set in memory.
/// </summary>
public class PgVectorRagVectorStore(
    ApplicationDbContext dbContext,
    IAppSettingsService appSettingsService,
    IRetrievalTextScorer scorer,
    ILogger<PgVectorRagVectorStore> logger) : IRagVectorStore
{
    public async Task<IReadOnlyList<RetrievalCandidate>> SearchAsync(
        RetrievalRequest request,
        CancellationToken cancellationToken = default)
    {
        var vectorStoreOptions = await appSettingsService.GetVectorStoreOptionsAsync(cancellationToken);
        var limit = Math.Clamp(vectorStoreOptions.CandidateLimit, 50, 10000);
        var queryVector = request.QueryEmbedding.Length > 0 ? new Vector(request.QueryEmbedding) : null;
        var query = dbContext.KnowledgeDocumentChunks
            .Include(chunk => chunk.Document)
            .Where(chunk => chunk.Document != null && chunk.Document.IsEnabled);

        if (!string.IsNullOrWhiteSpace(request.ModuleName))
        {
            var moduleName = KnowledgeModuleNames.Normalize(request.ModuleName);
            query = query.Where(chunk => chunk.Document!.ModuleName == moduleName);
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
                !MatchesKeywords(request, chunk.Document, chunk.ChunkText) ||
                !MatchesFilters(request, chunk.Document, chunk.ChunkText))
            {
                continue;
            }

            results.Add(new RetrievalCandidate(
                chunk.Document,
                chunk.ChunkText,
                chunk.Embedding.ToArray(),
                scorer.ScoreDocument(request, chunk.Document, chunk.ChunkText)));
        }

        logger.LogDebug("PgVector document search produced {CandidateCount} candidates.", results.Count);
        return results;
    }

    private static bool MatchesKeywords(
        RetrievalRequest request,
        KnowledgeDocument document,
        string chunkText)
    {
        if (request.Keywords.Count == 0)
        {
            return true;
        }

        var searchable = RetrievalTextScorer.BuildStructuredDocumentText(document) + " " + chunkText;
        return request.Keywords.Take(6)
            .Any(keyword => searchable.Contains(keyword, StringComparison.OrdinalIgnoreCase));
    }

    private static bool MatchesFilters(
        RetrievalRequest request,
        KnowledgeDocument document,
        string chunkText)
    {
        if (request.Filters.Count == 0)
        {
            return true;
        }

        var searchable = string.Join(
            ' ',
            document.Title,
            document.ModuleName,
            document.SourceType,
            document.Vendor,
            document.Product,
            document.Tags,
            chunkText);

        return request.Filters.Values
            .Where(value => !string.IsNullOrWhiteSpace(value) &&
                            !string.Equals(value, "none", StringComparison.OrdinalIgnoreCase))
            .All(value => searchable.Contains(value!.Trim(), StringComparison.OrdinalIgnoreCase));
    }
}
