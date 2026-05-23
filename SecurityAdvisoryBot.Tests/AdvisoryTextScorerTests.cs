using SecurityAdvisoryBot.Models;
using SecurityAdvisoryBot.Services;
using Xunit;

namespace SecurityAdvisoryBot.Tests;

public class AdvisoryTextScorerTests
{
    private static readonly AdvisoryTextScorer Scorer = new();

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
    public void ScoreAdvisory_KeywordInStructuredFields_ScoresHigher()
    {
        var request = BuildAdvisoryRequest(keywords: ["citrix"]);
        var inStructured = BuildAdvisory(vendor: "Citrix");
        var inChunkOnly = BuildAdvisory(vendor: "Unknown", chunkText: "citrix mentioned here");

        var structuredScore = Scorer.ScoreAdvisory(request, inStructured, string.Empty);
        var chunkScore = Scorer.ScoreAdvisory(request, inChunkOnly, "citrix mentioned here");

        Assert.True(structuredScore > chunkScore,
            $"Structured score {structuredScore} should exceed chunk-only score {chunkScore}");
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

        var score = Scorer.ScoreDocument(request, document, string.Empty);

        Assert.True(score >= 4, $"Expected score >= 4 for two keyword hits, got {score}");
    }

    // ── Helpers ──────────────────────────────────────────────────────────────

    private static AdvisoryVectorSearchRequest BuildAdvisoryRequest(
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
