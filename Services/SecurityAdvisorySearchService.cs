using System.Text.Json;
using SecurityAdvisoryBot.Data;
using SecurityAdvisoryBot.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace SecurityAdvisoryBot.Services;

public class SecurityAdvisorySearchService(
    ApplicationDbContext dbContext,
    IAdvisoryEmbeddingService embeddingService,
    IOptions<SecurityAdvisoryOptions> options,
    ILogger<SecurityAdvisorySearchService> logger) : ISecurityAdvisorySearchService
{
    public async Task<IReadOnlyList<SecurityAdvisorySearchResult>> SearchAsync(
        string question,
        int maxResults = 5,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(question))
        {
            return [];
        }

        var queryVector = await embeddingService.BuildEmbeddingAsync(question, cancellationToken);
        var effectiveMax = Math.Clamp(maxResults, 1, Math.Max(1, options.Value.RagMaxChunks));

        var chunks = await dbContext.SecurityAdvisoryChunks
            .Include(chunk => chunk.Advisory)
            .OrderByDescending(chunk => chunk.SecurityAdvisoryId)
            .Take(2000)
            .ToListAsync(cancellationToken);

        var ranked = new List<SecurityAdvisorySearchResult>();
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

            var score = CosineSimilarity(queryVector, vector);
            if (score <= 0)
            {
                continue;
            }

            ranked.Add(new SecurityAdvisorySearchResult(chunk.Advisory, chunk.ChunkText, score));
        }

        return ranked
            .OrderByDescending(item => item.Score)
            .Take(effectiveMax)
            .ToList();
    }

    private static double CosineSimilarity(IReadOnlyList<float> left, IReadOnlyList<float> right)
    {
        var length = Math.Min(left.Count, right.Count);
        if (length == 0)
        {
            return 0;
        }

        var sum = 0d;
        for (var index = 0; index < length; index++)
        {
            sum += left[index] * right[index];
        }

        return sum;
    }
}
