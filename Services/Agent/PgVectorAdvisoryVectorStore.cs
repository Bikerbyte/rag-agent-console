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

        var candidates = new List<AdvisoryVectorSearchCandidate>();
        foreach (var chunk in chunks)
        {
            if (chunk.Advisory is null || !MatchesRequest(request, chunk.Advisory, chunk.ChunkText))
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
                logger.LogDebug(exception, "Skipping malformed pgvector candidate embedding for chunk {ChunkId}.", chunk.SecurityAdvisoryChunkId);
                continue;
            }

            if (vector is null || vector.Length == 0)
            {
                continue;
            }

            candidates.Add(new AdvisoryVectorSearchCandidate(
                chunk.Advisory,
                chunk.ChunkText,
                vector,
                ScoreTextMatch(request, chunk.Advisory, chunk.ChunkText)));
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

    private static bool MatchesRequest(
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

        var searchable = BuildStructuredSearchableText(advisory);
        return request.Keywords.Take(6).Any(keyword => searchable.Contains(keyword, StringComparison.OrdinalIgnoreCase));
    }

    private static double ScoreTextMatch(
        AdvisoryVectorSearchRequest request,
        SecurityAdvisory advisory,
        string chunkText)
    {
        var score = 0d;
        var structuredSearchable = BuildStructuredSearchableText(advisory);
        var fullSearchable = string.Join(' ', structuredSearchable, advisory.Description, chunkText).ToLowerInvariant();

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

public sealed class PgVectorUnavailableException(string message) : InvalidOperationException(message);
