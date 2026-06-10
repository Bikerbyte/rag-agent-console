using RagAgentConsole.Data;
using RagAgentConsole.Models;
using RagAgentConsole.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace RagAgentConsole.Tests;

public class KnowledgeDocumentBatchOperationTests
{
    [Fact]
    public async Task SetEnabledManyAsync_DisablesAllSelectedDocuments()
    {
        var db = NewDb();
        var ids = SeedDocuments(db, 3);
        var service = CreateService(db);

        var affected = await service.SetEnabledManyAsync(ids, isEnabled: false);

        Assert.Equal(3, affected);
        var documents = await db.KnowledgeDocuments.ToListAsync();
        Assert.All(documents, document =>
        {
            Assert.False(document.IsEnabled);
            Assert.Equal("Disabled", document.Status);
        });
    }

    [Fact]
    public async Task SetEnabledManyAsync_ReenablesAndIgnoresUnknownIds()
    {
        var db = NewDb();
        var ids = SeedDocuments(db, 2, enabled: false);
        var service = CreateService(db);

        var affected = await service.SetEnabledManyAsync([.. ids, 999], isEnabled: true);

        Assert.Equal(2, affected);
        Assert.All(await db.KnowledgeDocuments.ToListAsync(), document =>
        {
            Assert.True(document.IsEnabled);
            Assert.Equal("Available", document.Status);
        });
    }

    [Fact]
    public async Task DeleteManyAsync_RemovesSelectedDocumentsOnly()
    {
        var db = NewDb();
        var ids = SeedDocuments(db, 3);
        var service = CreateService(db);

        var affected = await service.DeleteManyAsync(ids.Take(2).ToList());

        Assert.Equal(2, affected);
        var remaining = await db.KnowledgeDocuments.ToListAsync();
        Assert.Single(remaining);
        Assert.Equal(ids[2], remaining[0].KnowledgeDocumentId);
    }

    [Fact]
    public async Task BatchOperations_EmptySelection_AffectNothing()
    {
        var db = NewDb();
        SeedDocuments(db, 2);
        var service = CreateService(db);

        Assert.Equal(0, await service.SetEnabledManyAsync([], isEnabled: false));
        Assert.Equal(0, await service.DeleteManyAsync([]));
        Assert.Equal(2, await db.KnowledgeDocuments.CountAsync());
    }

    private static KnowledgeDocumentIngestionService CreateService(ApplicationDbContext db)
        => new(
            db,
            new KnowledgeDocumentTextExtractor(),
            new KnowledgeTextChunkingService(),
            new FakeEmbeddingService(),
            new FakeBm25Index(),
            NullLogger<KnowledgeDocumentIngestionService>.Instance);

    private static ApplicationDbContext NewDb()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase("doc-batch-tests-" + Guid.NewGuid().ToString("N"))
            .Options;
        return new ApplicationDbContext(options);
    }

    private static List<int> SeedDocuments(ApplicationDbContext db, int count, bool enabled = true)
    {
        var now = DateTimeOffset.UtcNow;
        for (var index = 0; index < count; index++)
        {
            db.KnowledgeDocuments.Add(new KnowledgeDocument
            {
                ModuleName = KnowledgeModuleNames.InternalDocs,
                Title = $"Doc {index + 1}",
                SourceType = "Upload",
                ExtractedText = "text",
                ContentHash = $"hash-{index}",
                IsEnabled = enabled,
                Status = enabled ? "Available" : "Disabled",
                CreatedTime = now,
                LastUpdatedTime = now
            });
        }

        db.SaveChanges();
        return db.KnowledgeDocuments.Select(document => document.KnowledgeDocumentId).OrderBy(id => id).ToList();
    }

    private sealed class FakeEmbeddingService : IRagEmbeddingService
    {
        public Task<float[]> BuildEmbeddingAsync(string text, CancellationToken cancellationToken = default)
            => Task.FromResult(new float[] { 1f });
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
