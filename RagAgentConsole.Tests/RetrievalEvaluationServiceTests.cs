using System.Text.Json;
using RagAgentConsole.Data;
using RagAgentConsole.Models;
using RagAgentConsole.Services;
using Microsoft.AspNetCore.Hosting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace RagAgentConsole.Tests;

public class RetrievalEvaluationServiceTests : IDisposable
{
    private readonly string _tempRoot;
    private readonly FakeHostEnvironment _environment;

    public RetrievalEvaluationServiceTests()
    {
        _tempRoot = Path.Combine(Path.GetTempPath(), "advisory-eval-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(_tempRoot, "Evaluation"));
        _environment = new FakeHostEnvironment { ContentRootPath = _tempRoot };
    }

    public void Dispose()
    {
        try { Directory.Delete(_tempRoot, recursive: true); } catch { /* best effort */ }
    }

    [Fact]
    public async Task EvaluateAsync_FirstResultIsRelevant_ScoresPerfect()
    {
        var db = NewDb();
        SeedCases(db, [("c1", "q", new[] { "CVE-2024-1234" })]);
        var search = new ScriptedSearchService([Response("CVE-2024-1234", "CVE-OTHER")]);
        var service = CreateService(search, db);

        var report = await service.EvaluateAsync(retrievalModes: [RetrievalModes.Hybrid]);
        var summary = Assert.Single(report.Summaries);

        Assert.Equal(1.0, summary.HitAt1);
        Assert.Equal(1.0, summary.HitAt5);
        Assert.Equal(1.0, summary.MeanReciprocalRank);
    }

    [Fact]
    public async Task EvaluateAsync_RelevantAtRankThree_ReciprocalRankIsOneThird()
    {
        var db = NewDb();
        SeedCases(db, [("c1", "q", new[] { "CVE-HIT" })]);
        var search = new ScriptedSearchService([Response("CVE-MISS-1", "CVE-MISS-2", "CVE-HIT", "CVE-MISS-3")]);
        var service = CreateService(search, db);

        var report = await service.EvaluateAsync(retrievalModes: [RetrievalModes.Hybrid]);
        var summary = Assert.Single(report.Summaries);

        Assert.False(summary.CaseResults[0].HitAt1);
        Assert.True(summary.CaseResults[0].HitAt5);
        Assert.Equal(1.0 / 3.0, summary.MeanReciprocalRank, 5);
    }

    [Fact]
    public async Task EvaluateAsync_NoRelevantResults_ScoresZero()
    {
        var db = NewDb();
        SeedCases(db, [("c1", "q", new[] { "CVE-EXPECTED" })]);
        var search = new ScriptedSearchService([Response("CVE-MISS-1", "CVE-MISS-2")]);
        var service = CreateService(search, db);

        var report = await service.EvaluateAsync(retrievalModes: [RetrievalModes.Hybrid]);
        var summary = Assert.Single(report.Summaries);

        Assert.Equal(0.0, summary.HitAt1);
        Assert.Equal(0.0, summary.HitAt5);
        Assert.Equal(0.0, summary.MeanReciprocalRank);
    }

    [Fact]
    public async Task EvaluateAsync_MultipleCases_AveragesMrrAcrossCases()
    {
        // Case 1 hit at rank 1 (RR=1), case 2 hit at rank 2 (RR=0.5).
        var db = NewDb();
        SeedCases(db,
        [
            ("c1", "q1", new[] { "CVE-A" }),
            ("c2", "q2", new[] { "CVE-B" })
        ]);
        var search = new ScriptedSearchService(
        [
            Response("CVE-A"),
            Response("CVE-MISS", "CVE-B")
        ]);
        var service = CreateService(search, db);

        var report = await service.EvaluateAsync(retrievalModes: [RetrievalModes.Hybrid]);
        var summary = Assert.Single(report.Summaries);

        Assert.Equal(0.75, summary.MeanReciprocalRank, 5);
    }

    [Fact]
    public async Task EvaluateAsync_MultipleStrategies_ReportsOneSummaryPerMode()
    {
        var db = NewDb();
        SeedCases(db, [("c1", "q", new[] { "CVE-A" })]);
        var search = new ScriptedSearchService(Enumerable.Repeat(Response("CVE-A"), 3).ToList());
        var service = CreateService(search, db);

        var report = await service.EvaluateAsync(retrievalModes:
            [RetrievalModes.Hybrid, RetrievalModes.Vector, RetrievalModes.Keyword]);

        Assert.Equal(3, report.Summaries.Count);
        Assert.Equal([RetrievalModes.Hybrid, RetrievalModes.Vector, RetrievalModes.Keyword],
            report.Summaries.Select(item => item.RetrievalMode));
    }

    [Fact]
    public async Task LoadCasesAsync_NoCases_ReturnsEmpty()
    {
        var db = NewDb();
        var service = CreateService(new ScriptedSearchService([]), db);

        var cases = await service.LoadCasesAsync();

        Assert.Empty(cases);
    }

    [Fact]
    public async Task SeedCasesIfEmptyAsync_LoadsBundledGoldenSet_OnlyWhenEmpty()
    {
        WriteGoldenSet([("kev-citrix", "Citrix NetScaler", new[] { "CVE-2023-3519" })]);
        var db = NewDb();
        var service = CreateService(new ScriptedSearchService([]), db);

        await service.SeedCasesIfEmptyAsync();
        var afterFirst = await service.LoadCasesAsync();

        // Running again must not duplicate the seed.
        await service.SeedCasesIfEmptyAsync();
        var afterSecond = await service.LoadCasesAsync();

        Assert.Single(afterFirst);
        Assert.Single(afterSecond);
        Assert.Equal("kev-citrix", afterFirst[0].Id);
        Assert.Equal(["CVE-2023-3519"], afterFirst[0].ExpectedCveIds);
    }

    [Fact]
    public async Task CreateThenDeleteCase_RoundTripsThroughDatabase()
    {
        var db = NewDb();
        var service = CreateService(new ScriptedSearchService([]), db);

        await service.CreateCaseAsync(new RetrievalEvaluationCaseDraft(
            "firewall policy guide",
            ExpectedCveIdsText: null,
            ExpectedDocumentTitlesText: "Firewall policy\nFirewall configuration guide",
            Notes: "doc-only case"));

        var managed = await service.GetManagedCasesAsync();
        var created = Assert.Single(managed);
        Assert.Equal("firewall-policy-guide", created.CaseKey);

        var cases = await service.LoadCasesAsync();
        Assert.Equal(["Firewall policy", "Firewall configuration guide"], cases[0].ExpectedDocumentTitles);

        await service.DeleteCaseAsync(created.RetrievalEvaluationCaseId);
        Assert.Empty(await service.GetManagedCasesAsync());
    }

    // ── Helpers ──────────────────────────────────────────────────────────

    private RetrievalEvaluationService CreateService(IRagRetrievalService search, ApplicationDbContext dbContext)
        => new(search, dbContext, _environment, NullLogger<RetrievalEvaluationService>.Instance);

    private static ApplicationDbContext NewDb()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase("eval-tests-" + Guid.NewGuid().ToString("N"))
            .Options;
        return new ApplicationDbContext(options);
    }

    private static void SeedCases(ApplicationDbContext dbContext, IEnumerable<(string Id, string Question, string[] ExpectedCveIds)> cases)
    {
        var now = DateTimeOffset.UtcNow;
        foreach (var item in cases)
        {
            dbContext.RetrievalEvaluationCases.Add(new RetrievalEvaluationCaseEntity
            {
                CaseKey = item.Id,
                Question = item.Question,
                ExpectedCveIds = string.Join('\n', item.ExpectedCveIds),
                CreatedTime = now,
                LastUpdatedTime = now
            });
        }

        dbContext.SaveChanges();
    }

    private void WriteGoldenSet(IEnumerable<(string Id, string Question, string[] ExpectedCveIds)> cases)
    {
        var payload = new
        {
            cases = cases.Select(item => new
            {
                id = item.Id,
                question = item.Question,
                expectedCveIds = item.ExpectedCveIds,
                expectedDocumentTitles = Array.Empty<string>(),
                notes = (string?)null
            })
        };
        var path = Path.Combine(_tempRoot, "Evaluation", "golden-set.json");
        File.WriteAllText(path, JsonSerializer.Serialize(payload));
    }

    private static RetrievalResponse Response(params string[] cveIds)
    {
        var results = cveIds.Select(cveId => new RetrievalResult(
            Advisory: new SecurityAdvisory
            {
                SourceName = "test",
                ExternalId = cveId,
                CveId = cveId,
                Title = cveId,
                Description = "",
                SourceUrl = "",
                ContentHash = ""
            },
            Document: null,
            ChunkText: "",
            Score: 1.0,
            VectorScore: 0.5,
            TextScore: 0.5)).ToList();

        var plan = new RetrievalPlan("q", "q", null, null, null, null, null, "none", [], [], KnowledgeModuleNames.CveAdvisory);
        return new RetrievalResponse(plan, RetrievalModes.Hybrid, results);
    }

    private sealed class ScriptedSearchService(IReadOnlyList<RetrievalResponse> responses) : IRagRetrievalService
    {
        private int _index;

        public Task<IReadOnlyList<RetrievalResult>> SearchAsync(string question, int maxResults = 5, CancellationToken cancellationToken = default)
            => Task.FromResult(Next().Results);

        public Task<IReadOnlyList<RetrievalResult>> SearchAsync(string question, IReadOnlyList<AgentConversationMessage>? history, int maxResults = 5, CancellationToken cancellationToken = default)
            => Task.FromResult(Next().Results);

        public Task<RetrievalResponse> SearchWithTraceAsync(string question, IReadOnlyList<AgentConversationMessage>? history = null, int maxResults = 5, string? moduleName = null, string retrievalMode = RetrievalModes.Hybrid, CancellationToken cancellationToken = default)
            => Task.FromResult(Next());

        private RetrievalResponse Next()
        {
            // Cycle if the test sets up fewer responses than calls;
            // for multi-strategy runs the same response is reused per strategy.
            if (responses.Count == 0)
            {
                throw new InvalidOperationException("No scripted responses provided.");
            }

            var response = responses[_index % responses.Count];
            _index++;
            return response;
        }
    }

    private sealed class FakeHostEnvironment : IWebHostEnvironment
    {
        public string WebRootPath { get; set; } = string.Empty;
        public IFileProvider WebRootFileProvider { get; set; } = new NullFileProvider();
        public string ApplicationName { get; set; } = "Test";
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
        public string ContentRootPath { get; set; } = AppContext.BaseDirectory;
        public string EnvironmentName { get; set; } = "Test";
    }
}
