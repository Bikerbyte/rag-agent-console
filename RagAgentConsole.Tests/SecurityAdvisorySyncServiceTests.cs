using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using RagAgentConsole.Data;
using RagAgentConsole.Models;
using RagAgentConsole.Services;
using Xunit;

namespace RagAgentConsole.Tests;

public class SecurityAdvisorySyncServiceTests
{
    [Fact]
    public async Task SyncAsync_UnchangedAdvisoryWithMissingEmbedding_RebuildsChunk()
    {
        await using var db = NewDb();
        var embeddingService = new FakeEmbeddingService();
        var service = CreateService(db, embeddingService);

        await service.SyncAsync();
        var chunk = await db.SecurityAdvisoryChunks.SingleAsync();
        chunk.Embedding = null;
        chunk.EmbeddingDimensions = 0;
        await db.SaveChangesAsync();

        var result = await service.SyncAsync();

        var rebuilt = await db.SecurityAdvisoryChunks.SingleAsync();
        Assert.NotNull(rebuilt.Embedding);
        Assert.Equal(3, rebuilt.EmbeddingDimensions);
        Assert.Equal(1, result.UpdatedCount);
        Assert.Equal(1, result.ChunkCount);
        Assert.Equal(2, embeddingService.CallCount);
    }

    private static SecurityAdvisorySyncService CreateService(
        ApplicationDbContext db,
        FakeEmbeddingService embeddingService)
        => new(
            db,
            [new FakeAdvisorySource()],
            embeddingService,
            new FakeBm25Index(),
            Options.Create(new AppRuntimeOptions { InstanceName = "test" }),
            NullLogger<SecurityAdvisorySyncService>.Instance);

    private static ApplicationDbContext NewDb()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase("advisory-sync-tests-" + Guid.NewGuid().ToString("N"))
            .Options;
        return new ApplicationDbContext(options);
    }

    private sealed class FakeAdvisorySource : ISecurityAdvisorySource
    {
        public string SourceName => "Test";

        public Task<IReadOnlyList<SecurityAdvisoryCandidate>> FetchLatestAsync(
            CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<SecurityAdvisoryCandidate>>(
            [
                new(
                    SourceName,
                    "CVE-2026-0001",
                    "CVE-2026-0001",
                    "Test advisory",
                    "Test description",
                    "Vendor",
                    "Product",
                    "High",
                    8.1m,
                    false,
                    false,
                    "Patch",
                    null,
                    DateTimeOffset.Parse("2026-06-01T00:00:00Z"),
                    DateTimeOffset.Parse("2026-06-01T00:00:00Z"),
                    "https://example.test/CVE-2026-0001")
            ]);
    }

    private sealed class FakeEmbeddingService : IRagEmbeddingService
    {
        public int CallCount { get; private set; }

        public Task<float[]> BuildEmbeddingAsync(string text, CancellationToken cancellationToken = default)
        {
            CallCount++;
            return Task.FromResult(new[] { 1f, 2f, 3f });
        }
    }

    private sealed class FakeBm25Index : IBm25Index
    {
        public bool IsBuilt => true;
        public int DocumentCount => 0;
        public double AverageDocumentLength => 0;
        public double Score(IReadOnlyList<string> queryTokens, IReadOnlyList<string> documentTokens) => 0;
        public Task RebuildAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
        public void RebuildFromCorpus(IEnumerable<string> documents) { }
    }
}
