using RagAgentConsole.Models;
using RagAgentConsole.Services;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace RagAgentConsole.Tests;

public class RetrievalTextScorerTests
{
    private static RetrievalTextScorer CreateScorer(IEnumerable<string>? corpus = null)
    {
        var tokenizer = new MixedScriptTokenizer();
        var index = new InMemoryBm25Index(
            scopeFactory: null!,
            tokenizer,
            NullLogger<InMemoryBm25Index>.Instance);

        // Seed the index with a small synthetic corpus so IDF / avgdl
        // are non-degenerate during scoring. Tests that need specific
        // corpus statistics can pass their own.
        index.RebuildFromCorpus(corpus ?? new[]
        {
            "cve-2024-0001 cisco ios denial of service",
            "cve-2024-0002 fortinet fortigate buffer overflow",
            "cve-2024-1234 citrix netscaler authentication bypass",
            "microsoft windows kernel privilege escalation",
            "firewall configuration policy guide",
            "antivirus deployment checklist"
        });

        return new RetrievalTextScorer(index, tokenizer);
    }

    private static readonly RetrievalTextScorer Scorer = CreateScorer();

    // ── ScoreAdvisory ────────────────────────────────────────────────────────

    [Fact]
    public void ScoreAdvisory_ExactCveIdMatch_AddsBonus()
    {
        var request = BuildAdvisoryRequest(cveId: "CVE-2024-1234");
        var advisory = BuildAdvisory(cveId: "CVE-2024-1234");

        var score = Scorer.ScoreAdvisory(request, advisory, string.Empty);

        Assert.True(score >= 4, $"Expected score >= 4 for CveId match, got {score}");
    }

    [Fact]
    public void ScoreAdvisory_ExternalIdMatchesCveId_AddsBonus()
    {
        var request = BuildAdvisoryRequest(cveId: "CVE-2024-1234");
        var advisory = BuildAdvisory(externalId: "CVE-2024-1234");

        var score = Scorer.ScoreAdvisory(request, advisory, string.Empty);

        Assert.True(score >= 4, $"Expected score >= 4 for ExternalId match, got {score}");
    }

    [Fact]
    public void ScoreAdvisory_MoreTermOccurrences_ScoresHigher()
    {
        // BM25 rewards higher term frequency (with saturation via k1).
        var request = BuildAdvisoryRequest(keywords: ["citrix"]);
        var single = BuildAdvisory(vendor: "Citrix", title: "Generic vulnerability", chunkText: "no match here");
        var multiple = BuildAdvisory(vendor: "Citrix", title: "Citrix NetScaler vulnerability", chunkText: "citrix appliance impacted");

        var singleScore = Scorer.ScoreAdvisory(request, single, "no match here");
        var multipleScore = Scorer.ScoreAdvisory(request, multiple, "citrix appliance impacted");

        Assert.True(multipleScore > singleScore,
            $"Higher TF should produce higher BM25 score; got single={singleScore}, multiple={multipleScore}");
    }

    [Fact]
    public void ScoreAdvisory_RareTerm_ScoresHigherThanCommonTerm()
    {
        // BM25 IDF: terms that appear in fewer docs in the corpus weigh more.
        // Build a custom corpus where "common" appears in every doc and "rare" appears in only one.
        var scorer = CreateScorer(new[]
        {
            "common term appears here",
            "common term again here",
            "common term once more",
            "rare term shows up once"
        });

        var rareReq = BuildAdvisoryRequest(keywords: ["rare"]);
        var commonReq = BuildAdvisoryRequest(keywords: ["common"]);
        var advisory = BuildAdvisory(title: "rare common", chunkText: "rare common");

        var rareScore = scorer.ScoreAdvisory(rareReq, advisory, "rare common");
        var commonScore = scorer.ScoreAdvisory(commonReq, advisory, "rare common");

        Assert.True(rareScore > commonScore,
            $"Rare term should score higher than common term; got rare={rareScore}, common={commonScore}");
    }

    [Fact]
    public void ScoreAdvisory_KeywordNotPresent_ScoresZero()
    {
        var request = BuildAdvisoryRequest(keywords: ["fortinet"]);
        var advisory = BuildAdvisory(vendor: "Cisco", title: "Cisco IOS vulnerability");

        var score = Scorer.ScoreAdvisory(request, advisory, string.Empty);

        Assert.Equal(0, score);
    }

    [Fact]
    public void ScoreAdvisory_KevOnlyFilter_AddsBonus()
    {
        var request = BuildAdvisoryRequest(kevOnly: true);
        var kev = BuildAdvisory(isKnownExploited: true);
        var nonKev = BuildAdvisory(isKnownExploited: false);

        var kevScore = Scorer.ScoreAdvisory(request, kev, string.Empty);
        var nonKevScore = Scorer.ScoreAdvisory(request, nonKev, string.Empty);

        Assert.True(kevScore > nonKevScore);
    }

    [Fact]
    public void ScoreAdvisory_HighRiskFilter_AddsBonus()
    {
        var request = BuildAdvisoryRequest(highRiskOnly: true);
        var highRisk = BuildAdvisory(cvssScore: 9.8m);
        var lowRisk = BuildAdvisory(cvssScore: 4.0m);

        var highScore = Scorer.ScoreAdvisory(request, highRisk, string.Empty);
        var lowScore = Scorer.ScoreAdvisory(request, lowRisk, string.Empty);

        Assert.True(highScore > lowScore);
    }

    // ── ScoreDocument ────────────────────────────────────────────────────────

    [Fact]
    public void ScoreDocument_KeywordInTitle_ScoresHigher()
    {
        var request = BuildAdvisoryRequest(keywords: ["firewall"]);
        var inTitle = BuildDocument(title: "Firewall configuration guide");
        var inChunkOnly = BuildDocument(title: "General IT guide", chunkText: "firewall rules");

        var titleScore = Scorer.ScoreDocument(request, inTitle, string.Empty);
        var chunkScore = Scorer.ScoreDocument(request, inChunkOnly, "firewall rules");

        Assert.True(titleScore > chunkScore,
            $"Title score {titleScore} should exceed chunk-only score {chunkScore}");
    }

    [Fact]
    public void ScoreDocument_KeywordAbsent_ScoresZero()
    {
        var request = BuildAdvisoryRequest(keywords: ["antivirus"]);
        var document = BuildDocument(title: "Network monitoring setup");

        var score = Scorer.ScoreDocument(request, document, string.Empty);

        Assert.Equal(0, score);
    }

    [Fact]
    public void ScoreDocument_MultipleKeywordMatches_AccumulatesScore()
    {
        var request = BuildAdvisoryRequest(keywords: ["firewall", "policy"]);
        var document = BuildDocument(title: "Firewall policy", moduleName: "InternalDocs");
        var singleKeywordReq = BuildAdvisoryRequest(keywords: ["firewall"]);

        var twoScore = Scorer.ScoreDocument(request, document, string.Empty);
        var oneScore = Scorer.ScoreDocument(singleKeywordReq, document, string.Empty);

        Assert.True(twoScore > oneScore,
            $"Two keyword matches should outscore one; got two={twoScore}, one={oneScore}");
        Assert.True(twoScore > 0);
    }

    // ── Helpers ──────────────────────────────────────────────────────────────

    private static RetrievalRequest BuildAdvisoryRequest(
        string? cveId = null,
        IReadOnlyList<string>? keywords = null,
        bool kevOnly = false,
        bool highRiskOnly = false)
        => new(
            Question: "test query",
            CveId: cveId,
            Version: null,
            KevOnly: kevOnly,
            HighRiskOnly: highRiskOnly,
            Keywords: keywords ?? [],
            QueryEmbedding: [],
            MaxResults: 5,
            ModuleName: KnowledgeModuleNames.CveAdvisory,
            RetrievalMode: RetrievalModes.Hybrid);

    private static SecurityAdvisory BuildAdvisory(
        string? cveId = null,
        string? externalId = null,
        string vendor = "TestVendor",
        string product = "TestProduct",
        string? title = null,
        bool isKnownExploited = false,
        decimal? cvssScore = null,
        string? severity = null,
        string? chunkText = null)
        => new()
        {
            SourceName = "test",
            ExternalId = externalId ?? "EXT-001",
            CveId = cveId,
            Title = title ?? $"{vendor} {product} vulnerability",
            Description = chunkText ?? "test description",
            Vendor = vendor,
            Product = product,
            IsKnownExploited = isKnownExploited,
            CvssScore = cvssScore,
            Severity = severity,
            SourceUrl = "https://example.com",
            ContentHash = "hash"
        };

    private static KnowledgeDocument BuildDocument(
        string title = "Test Document",
        string moduleName = KnowledgeModuleNames.InternalDocs,
        string? chunkText = null)
        => new()
        {
            Title = title,
            ModuleName = moduleName,
            SourceType = "manual",
            Vendor = null,
            Product = null,
            Tags = null,
            ExtractedText = chunkText ?? string.Empty,
            ContentHash = "hash"
        };
}
