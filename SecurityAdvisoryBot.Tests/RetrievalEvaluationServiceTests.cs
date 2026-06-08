using System.Text.Json;
using SecurityAdvisoryBot.Models;
using SecurityAdvisoryBot.Services;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace SecurityAdvisoryBot.Tests;

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
        WriteGoldenSet([("c1", "q", new[] { "CVE-2024-1234" })]);
        var search = new ScriptedSearchService([Response("CVE-2024-1234", "CVE-OTHER")]);
        var service = CreateService(search);

        var report = await service.EvaluateAsync(retrievalModes: [RetrievalModes.Hybrid]);
        var summary = Assert.Single(report.Summaries);

        Assert.Equal(1.0, summary.HitAt1);
        Assert.Equal(1.0, summary.HitAt5);
        Assert.Equal(1.0, summary.MeanReciprocalRank);
    }

    [Fact]
    public async Task EvaluateAsync_RelevantAtRankThree_ReciprocalRankIsOneThird()
    {
        WriteGoldenSet([("c1", "q", new[] { "CVE-HIT" })]);
        var search = new ScriptedSearchService([Response("CVE-MISS-1", "CVE-MISS-2", "CVE-HIT", "CVE-MISS-3")]);
        var service = CreateService(search);

        var report = await service.EvaluateAsync(retrievalModes: [RetrievalModes.Hybrid]);
        var summary = Assert.Single(report.Summaries);

        Assert.False(summary.CaseResults[0].HitAt1);
        Assert.True(summary.CaseResults[0].HitAt5);
        Assert.Equal(1.0 / 3.0, summary.MeanReciprocalRank, 5);
    }

    [Fact]
    public async Task EvaluateAsync_NoRelevantResults_ScoresZero()
    {
        WriteGoldenSet([("c1", "q", new[] { "CVE-EXPECTED" })]);
        var search = new ScriptedSearchService([Response("CVE-MISS-1", "CVE-MISS-2")]);
        var service = CreateService(search);

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
        WriteGoldenSet(
        [
            ("c1", "q1", new[] { "CVE-A" }),
            ("c2", "q2", new[] { "CVE-B" })
        ]);
        var search = new ScriptedSearchService(
        [
            Response("CVE-A"),
            Response("CVE-MISS", "CVE-B")
        ]);
        var service = CreateService(search);

        var report = await service.EvaluateAsync(retrievalModes: [RetrievalModes.Hybrid]);
        var summary = Assert.Single(report.Summaries);

        Assert.Equal(0.75, summary.MeanReciprocalRank, 5);
    }

    [Fact]
    public async Task EvaluateAsync_MultipleStrategies_ReportsOneSummaryPerMode()
    {
        WriteGoldenSet([("c1", "q", new[] { "CVE-A" })]);
        var search = new ScriptedSearchService(Enumerable.Repeat(Response("CVE-A"), 3).ToList());
        var service = CreateService(search);

        var report = await service.EvaluateAsync(retrievalModes:
            [RetrievalModes.Hybrid, RetrievalModes.Vector, RetrievalModes.Keyword]);

        Assert.Equal(3, report.Summaries.Count);
        Assert.Equal([RetrievalModes.Hybrid, RetrievalModes.Vector, RetrievalModes.Keyword],
            report.Summaries.Select(item => item.RetrievalMode));
    }

    [Fact]
    public async Task LoadCasesAsync_MissingFile_ReturnsEmpty()
    {
        // No file written.
        var search = new ScriptedSearchService([]);
        var service = CreateService(search);

        var cases = await service.LoadCasesAsync();

        Assert.Empty(cases);
    }

    // ── Helpers ──────────────────────────────────────────────────────────

    private RetrievalEvaluationService CreateService(ISecurityAdvisorySearchService search)
        => new(search, _environment, NullLogger<RetrievalEvaluationService>.Instance);

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

    private static SecurityAdvisorySearchResponse Response(params string[] cveIds)
    {
        var results = cveIds.Select(cveId => new SecurityAdvisorySearchResult(
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

        var plan = new AdvisoryQueryPlan("q", "q", null, null, null, null, null, "none", [], [], KnowledgeModuleNames.CveAdvisory);
        return new SecurityAdvisorySearchResponse(plan, RetrievalModes.Hybrid, results);
    }

    private sealed class ScriptedSearchService(IReadOnlyList<SecurityAdvisorySearchResponse> responses) : ISecurityAdvisorySearchService
    {
        private int _index;

        public Task<IReadOnlyList<SecurityAdvisorySearchResult>> SearchAsync(string question, int maxResults = 5, CancellationToken cancellationToken = default)
            => Task.FromResult(Next().Results);

        public Task<IReadOnlyList<SecurityAdvisorySearchResult>> SearchAsync(string question, IReadOnlyList<AdvisoryConversationMessage>? history, int maxResults = 5, CancellationToken cancellationToken = default)
            => Task.FromResult(Next().Results);

        public Task<SecurityAdvisorySearchResponse> SearchWithTraceAsync(string question, IReadOnlyList<AdvisoryConversationMessage>? history = null, int maxResults = 5, string? moduleName = null, string retrievalMode = RetrievalModes.Hybrid, CancellationToken cancellationToken = default)
            => Task.FromResult(Next());

        private SecurityAdvisorySearchResponse Next()
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
