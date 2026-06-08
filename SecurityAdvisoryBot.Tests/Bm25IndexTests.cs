using SecurityAdvisoryBot.Services;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace SecurityAdvisoryBot.Tests;

public class Bm25IndexTests
{
    [Fact]
    public void Score_BeforeRebuild_ReturnsZero()
    {
        var index = CreateIndex();

        var score = index.Score(["firewall"], ["firewall", "policy"]);

        Assert.Equal(0, score);
    }

    [Fact]
    public void Score_TermNotInCorpus_DoesNotContribute()
    {
        var index = CreateIndex();
        index.RebuildFromCorpus(new[]
        {
            "firewall configuration guide",
            "antivirus deployment notes"
        });

        var presentScore = index.Score(["firewall"], ["firewall", "rules"]);
        var absentScore = index.Score(["nonexistent"], ["firewall", "rules"]);

        Assert.True(presentScore > 0);
        Assert.Equal(0, absentScore);
    }

    [Fact]
    public void Score_HigherTermFrequency_ProducesHigherScore()
    {
        var index = CreateIndex();
        index.RebuildFromCorpus(new[]
        {
            "alpha beta gamma",
            "alpha delta epsilon",
            "zeta eta theta"
        });

        var onceScore = index.Score(["alpha"], ["alpha", "filler", "filler", "filler"]);
        var twiceScore = index.Score(["alpha"], ["alpha", "alpha", "filler", "filler"]);

        Assert.True(twiceScore > onceScore,
            $"Higher TF should produce higher score; once={onceScore}, twice={twiceScore}");
    }

    [Fact]
    public void Score_RareTerm_HasHigherIdfThanCommonTerm()
    {
        var index = CreateIndex();
        index.RebuildFromCorpus(new[]
        {
            "common term here",
            "common term here",
            "common term here",
            "rare token shows once"
        });

        // Same TF (1) and same doc length, IDF dictates the difference.
        var rareScore = index.Score(["rare"], ["rare", "filler", "filler"]);
        var commonScore = index.Score(["common"], ["common", "filler", "filler"]);

        Assert.True(rareScore > commonScore,
            $"Rare term IDF should exceed common term IDF; rare={rareScore}, common={commonScore}");
    }

    [Fact]
    public void Score_LongerDocument_GetsLengthNormalizationPenalty()
    {
        var index = CreateIndex();
        index.RebuildFromCorpus(new[]
        {
            "alpha beta",
            "alpha gamma",
            "alpha delta",
            "zeta eta theta",
            "zeta eta iota",
            "kappa lambda mu"
        });

        // Same TF (1), but the longer doc gets penalized by b * |D|/avgdl.
        var shortScore = index.Score(["alpha"], ["alpha", "filler"]);
        var longScore = index.Score(["alpha"], new[] { "alpha" }.Concat(Enumerable.Repeat("filler", 30)).ToList());

        Assert.True(shortScore > longScore,
            $"Shorter doc should outscore longer doc with same TF; short={shortScore}, long={longScore}");
    }

    [Fact]
    public void Score_DeduplicatesRepeatedQueryTerms()
    {
        var index = CreateIndex();
        index.RebuildFromCorpus(new[]
        {
            "alpha beta gamma",
            "alpha delta",
            "zeta eta"
        });

        var singleScore = index.Score(["alpha"], ["alpha", "filler"]);
        var duplicateScore = index.Score(["alpha", "alpha", "alpha"], ["alpha", "filler"]);

        // Repeated query terms should not multiply the score.
        Assert.Equal(singleScore, duplicateScore);
    }

    [Fact]
    public void RebuildFromCorpus_PopulatesStats()
    {
        var index = CreateIndex();
        index.RebuildFromCorpus(new[]
        {
            "alpha beta",
            "gamma delta epsilon"
        });

        Assert.True(index.IsBuilt);
        Assert.Equal(2, index.DocumentCount);
        Assert.Equal(2.5, index.AverageDocumentLength);
    }

    private static InMemoryBm25Index CreateIndex()
        => new(scopeFactory: null!, new MixedScriptTokenizer(), NullLogger<InMemoryBm25Index>.Instance);
}
