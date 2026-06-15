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
        # Backup Recovery Standard

        Tier 1 systems require **AES-256** encrypted backups.
        """);

        var result = await extractor.ExtractAsync("backup-standard.md", "text/markdown", stream);

        Assert.Equal("Markdig", result.ParserName);
        Assert.Contains("Backup Recovery Standard", result.Text);
        Assert.Contains("Tier 1 systems require AES-256 encrypted backups.", result.Text);
        Assert.DoesNotContain("**", result.Text);
    }

    [Fact]
    public async Task ExtractAsync_WhenCsvFile_ConvertsRowsToReadableText()
    {
        var extractor = new KnowledgeDocumentTextExtractor();
        await using var stream = BuildStream("""
        system,owner,retention
        Finance,IT,730 days
        """);

        var result = await extractor.ExtractAsync("retention-policy.csv", "text/csv", stream);

        Assert.Equal("CsvHelper", result.ParserName);
        Assert.Contains("system | owner | retention", result.Text);
        Assert.Contains("Finance | IT | 730 days", result.Text);
    }

    [Fact]
    public void SplitIntoChunks_WhenTextIsLong_ReturnsNonEmptyChunks()
    {
        var chunkingService = new KnowledgeTextChunkingService();
        var text = string.Join(
            Environment.NewLine,
            Enumerable.Range(1, 80).Select(index => $"Line {index}: Backup recovery context and operational guidance."));

        var chunks = chunkingService.SplitIntoChunks(text);

        Assert.NotEmpty(chunks);
        Assert.All(chunks, chunk => Assert.Contains("Backup", chunk));
    }

    private static MemoryStream BuildStream(string value)
        => new(Encoding.UTF8.GetBytes(value));
}
