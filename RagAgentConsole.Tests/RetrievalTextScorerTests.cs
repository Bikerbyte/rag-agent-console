using RagAgentConsole.Models;
using RagAgentConsole.Services;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace RagAgentConsole.Tests;

public class RetrievalTextScorerTests
{
    [Fact]
    public void ScoreDocument_MatchingKeyword_ReturnsPositiveScore()
    {
        var scorer = CreateScorer();
        var request = BuildRequest("firewall");
        var document = BuildDocument("Firewall configuration guide");

        var score = scorer.ScoreDocument(request, document, "deny by default");

        Assert.True(score > 0);
    }

    [Fact]
    public void ScoreDocument_KeywordAbsent_ReturnsZero()
    {
        var scorer = CreateScorer();
        var request = BuildRequest("antivirus");
        var document = BuildDocument("Network monitoring setup");

        var score = scorer.ScoreDocument(request, document, "traffic dashboard");

        Assert.Equal(0, score);
    }

    [Fact]
    public void ScoreDocument_MoreTermOccurrences_ScoresHigher()
    {
        var scorer = CreateScorer();
        var request = BuildRequest("backup");
        var document = BuildDocument("Recovery guide");

        var single = scorer.ScoreDocument(request, document, "backup schedule");
        var multiple = scorer.ScoreDocument(request, document, "backup backup backup recovery");

        Assert.True(multiple > single);
    }

    [Fact]
    public void ScoreDocument_RareTerm_ScoresHigherThanCommonTerm()
    {
        var scorer = CreateScorer([
            "common policy text",
            "common workflow text",
            "common handbook text",
            "rare recovery control"
        ]);
        var document = BuildDocument("rare common");

        var rare = scorer.ScoreDocument(BuildRequest("rare"), document, "rare common");
        var common = scorer.ScoreDocument(BuildRequest("common"), document, "rare common");

        Assert.True(rare > common);
    }

    [Fact]
    public void ScoreDocument_MultipleKeywordMatches_Accumulate()
    {
        var scorer = CreateScorer();
        var document = BuildDocument("Firewall policy");

        var one = scorer.ScoreDocument(BuildRequest("firewall"), document, "policy rules");
        var two = scorer.ScoreDocument(BuildRequest("firewall", "policy"), document, "policy rules");

        Assert.True(two > one);
    }

    [Fact]
    public void ScoreDocument_EmptyKeywords_ReturnsZero()
    {
        var scorer = CreateScorer();
        var request = new RetrievalRequest("question", [], RetrievalPlan.EmptyValues, [], 5);

        Assert.Equal(0, scorer.ScoreDocument(request, BuildDocument("Policy"), "policy"));
    }

    private static RetrievalTextScorer CreateScorer(IEnumerable<string>? corpus = null)
    {
        var tokenizer = new MixedScriptTokenizer();
        var index = new InMemoryBm25Index(null!, tokenizer, NullLogger<InMemoryBm25Index>.Instance);
        index.RebuildFromCorpus(corpus ?? [
            "firewall configuration policy guide",
            "backup recovery standard",
            "password expiration policy",
            "incident response workflow"
        ]);
        return new RetrievalTextScorer(index, tokenizer);
    }

    private static RetrievalRequest BuildRequest(params string[] keywords)
        => new("question", keywords, RetrievalPlan.EmptyValues, [], 5);

    private static KnowledgeDocument BuildDocument(string title)
        => new()
        {
            Title = title,
            ModuleName = KnowledgeModuleNames.InternalDocs,
            SourceType = "test",
            ExtractedText = title,
            ContentHash = "hash"
        };
}
