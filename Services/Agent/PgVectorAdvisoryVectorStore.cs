using System.Data;
using System.Globalization;
using System.Text.Json;
using SecurityAdvisoryBot.Data;
using SecurityAdvisoryBot.Models;
using Microsoft.EntityFrameworkCore;

namespace SecurityAdvisoryBot.Services;

public class PgVectorAdvisoryVectorStore(
    ApplicationDbContext dbContext,
    IAppSettingsService appSettingsService,
    IAdvisoryTextScorer scorer,
    ILogger<PgVectorAdvisoryVectorStore> logger) : IAdvisoryVectorStore
{
    public async Task<IReadOnlyList<AdvisoryVectorSearchCandidate>> SearchAsync(
        AdvisoryVectorSearchRequest request,
        CancellationToken cancellationToken = default)
    {
        if (request.QueryEmbedding.Length == 0)
        {
            return [];
        }

        await EnsurePgVectorAvailableAsync(cancellationToken);

        var queryVector = BuildVectorLiteral(request.QueryEmbedding);
        var vectorStoreOptions = await appSettingsService.GetVectorStoreOptionsAsync(cancellationToken);
        var limit = Math.Clamp(vectorStoreOptions.CandidateLimit, 50, 10000);
        var candidates = new List<AdvisoryVectorSearchCandidate>();

        if (ShouldSearchAdvisories(request))
        {
            var chunks = await dbContext.SecurityAdvisoryChunks
                .FromSqlRaw(
                    """
                    SELECT *
                    FROM "SecurityAdvisoryChunks"
                    WHERE "EmbeddingJson" IS NOT NULL AND "EmbeddingJson" <> ''
                      AND vector_dims("EmbeddingJson"::vector) = {2}
                    ORDER BY "EmbeddingJson"::vector <=> CAST({0} AS vector)
                    LIMIT {1}
                    """,
                    queryVector,
                    limit,
                    request.QueryEmbedding.Length)
                .Include(chunk => chunk.Advisory)
                .AsNoTracking()
                .ToListAsync(cancellationToken);

            foreach (var chunk in chunks)
            {
                if (chunk.Advisory is null || !MatchesAdvisoryRequest(request, chunk.Advisory, chunk.ChunkText))
                {
                    continue;
                }

                var vector = TryReadVector(chunk.EmbeddingJson, "advisory", chunk.SecurityAdvisoryChunkId);
                if (vector is null)
                {
                    continue;
                }

                candidates.Add(new AdvisoryCandidate(
                    chunk.Advisory,
                    chunk.ChunkText,
                    vector,
                    scorer.ScoreAdvisory(request, chunk.Advisory, chunk.ChunkText)));
            }
        }

        var documentChunks = await dbContext.KnowledgeDocumentChunks
            .FromSqlRaw(
                """
                SELECT *
                FROM "KnowledgeDocumentChunks"
                WHERE "EmbeddingJson" IS NOT NULL AND "EmbeddingJson" <> ''
                  AND vector_dims("EmbeddingJson"::vector) = {2}
                ORDER BY "EmbeddingJson"::vector <=> CAST({0} AS vector)
                LIMIT {1}
                """,
                queryVector,
                limit,
                request.QueryEmbedding.Length)
            .Include(chunk => chunk.Document)
            .AsNoTracking()
            .ToListAsync(cancellationToken);

        foreach (var chunk in documentChunks)
        {
            if (chunk.Document is null || !MatchesDocumentRequest(request, chunk.Document, chunk.ChunkText))
            {
                continue;
            }

            var vector = TryReadVector(chunk.EmbeddingJson, "knowledge document", chunk.KnowledgeDocumentChunkId);
            if (vector is null)
            {
                continue;
            }

            candidates.Add(new DocumentCandidate(
                chunk.Document,
                chunk.ChunkText,
                vector,
                scorer.ScoreDocument(request, chunk.Document, chunk.ChunkText)));
        }

        logger.LogDebug("PgVector search produced {CandidateCount} candidates.", candidates.Count);
        return candidates;
    }

    private async Task EnsurePgVectorAvailableAsync(CancellationToken cancellationToken)
    {
        if (!string.Equals(dbContext.Database.ProviderName, "Npgsql.EntityFrameworkCore.PostgreSQL", StringComparison.Ordinal))
        {
            throw new PgVectorUnavailableException("PgVector store requires PostgreSQL.");
        }

        var connection = dbContext.Database.GetDbConnection();
        var openedHere = connection.State != ConnectionState.Open;
        if (openedHere)
        {
            await connection.OpenAsync(cancellationToken);
        }

        try
        {
            try
            {
                await using (var createCommand = connection.CreateCommand())
                {
                    createCommand.CommandText = "CREATE EXTENSION IF NOT EXISTS vector";
                    await createCommand.ExecuteNonQueryAsync(cancellationToken);
                }

                await using var command = connection.CreateCommand();
                command.CommandText = "SELECT EXISTS (SELECT 1 FROM pg_extension WHERE extname = 'vector')";
                var result = await command.ExecuteScalarAsync(cancellationToken);
                if (result is true)
                {
                    return;
                }
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception exception)
            {
                throw new PgVectorUnavailableException($"PostgreSQL vector extension is not available: {exception.Message}");
            }

            throw new PgVectorUnavailableException("PostgreSQL vector extension is not installed.");
        }
        finally
        {
            if (openedHere)
            {
                await connection.CloseAsync();
            }
        }
    }

    private static string BuildVectorLiteral(IReadOnlyList<float> vector)
        => "[" + string.Join(',', vector.Select(value => value.ToString("R", CultureInfo.InvariantCulture))) + "]";

    private float[]? TryReadVector(string embeddingJson, string sourceKind, int chunkId)
    {
        try
        {
            var vector = JsonSerializer.Deserialize<float[]>(embeddingJson);
            return vector is { Length: > 0 } ? vector : null;
        }
        catch (JsonException exception)
        {
            logger.LogDebug(exception, "Skipping malformed pgvector {SourceKind} embedding for chunk {ChunkId}.", sourceKind, chunkId);
            return null;
        }
    }

    private static bool ShouldSearchAdvisories(AdvisoryVectorSearchRequest request)
        => string.IsNullOrWhiteSpace(request.ModuleName) ||
           string.Equals(request.ModuleName, KnowledgeModuleNames.CveAdvisory, StringComparison.OrdinalIgnoreCase);

    private static bool MatchesAdvisoryRequest(
        AdvisoryVectorSearchRequest request,
        SecurityAdvisory advisory,
        string chunkText)
    {
        if (request.KevOnly && !advisory.IsKnownExploited)
        {
            return false;
        }

        if (request.HighRiskOnly &&
            !advisory.IsKnownExploited &&
            advisory.CvssScore < 9 &&
            !string.Equals(advisory.Severity, "Critical", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (request.Keywords.Count == 0)
        {
            return true;
        }

        var searchable = AdvisoryTextScorer.BuildStructuredAdvisoryText(advisory);
        return request.Keywords.Take(6).Any(keyword => searchable.Contains(keyword, StringComparison.OrdinalIgnoreCase));
    }

    private static bool MatchesDocumentRequest(
        AdvisoryVectorSearchRequest request,
        KnowledgeDocument document,
        string chunkText)
    {
        if (!document.IsEnabled)
        {
            return false;
        }

        if (!string.IsNullOrWhiteSpace(request.ModuleName) &&
            !string.Equals(request.ModuleName, document.ModuleName, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (request.Keywords.Count == 0)
        {
            return true;
        }

        var searchable = AdvisoryTextScorer.BuildStructuredDocumentText(document) + " " + chunkText;
        return request.Keywords.Take(6).Any(keyword => searchable.Contains(keyword, StringComparison.OrdinalIgnoreCase));
    }
}

public sealed class PgVectorUnavailableException(string message) : InvalidOperationException(message);
