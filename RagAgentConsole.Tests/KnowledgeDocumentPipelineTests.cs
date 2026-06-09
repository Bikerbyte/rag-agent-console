using System.Text;
using RagAgentConsole.Services;
using Xunit;

namespace RagAgentConsole.Tests;

public class KnowledgeDocumentPipelineTests
{
    [Fact]
    public async Task ExtractAsync_WhenMarkdownFile_RemovesMarkdownSyntax()
    {
        var extractor = new KnowledgeDocumentTextExtractor();
        await using var stream = BuildStream("""
        # NetScaler Advisory

        Citrix **NetScaler ADC** should restrict management access.
        """);

        var result = await extractor.ExtractAsync("advisory.md", "text/markdown", stream);

        Assert.Equal("Markdig", result.ParserName);
        Assert.Contains("NetScaler Advisory", result.Text);
        Assert.Contains("Citrix NetScaler ADC should restrict management access.", result.Text);
        Assert.DoesNotContain("**", result.Text);
    }

    [Fact]
    public async Task ExtractAsync_WhenCsvFile_ConvertsRowsToReadableText()
    {
        var extractor = new KnowledgeDocumentTextExtractor();
        await using var stream = BuildStream("""
        vendor,product,risk
        Citrix,NetScaler,critical
        """);

        var result = await extractor.ExtractAsync("advisories.csv", "text/csv", stream);

        Assert.Equal("CsvHelper", result.ParserName);
        Assert.Contains("vendor | product | risk", result.Text);
        Assert.Contains("Citrix | NetScaler | critical", result.Text);
    }

    [Fact]
    public void SplitIntoChunks_WhenTextIsLong_ReturnsNonEmptyChunks()
    {
        var chunkingService = new KnowledgeTextChunkingService();
        var text = string.Join(
            Environment.NewLine,
            Enumerable.Range(1, 80).Select(index => $"Line {index}: Citrix NetScaler advisory context and mitigation guidance."));

        var chunks = chunkingService.SplitIntoChunks(text);

        Assert.NotEmpty(chunks);
        Assert.All(chunks, chunk => Assert.Contains("NetScaler", chunk));
    }

    private static MemoryStream BuildStream(string value)
        => new(Encoding.UTF8.GetBytes(value));
}
