using Microsoft.SemanticKernel.Text;

namespace RagAgentConsole.Services;

#pragma warning disable SKEXP0050
public class KnowledgeTextChunkingService : IKnowledgeTextChunkingService
{
    private const int MaxTokensPerLine = 220;
    private const int MaxTokensPerParagraph = 720;
    private const int OverlapTokens = 80;

    public IReadOnlyList<string> SplitIntoChunks(string text, bool isMarkdown = false)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return [];
        }

        var lines = isMarkdown
            ? TextChunker.SplitMarkDownLines(text, MaxTokensPerLine)
            : TextChunker.SplitPlainTextLines(text, MaxTokensPerLine);

        var chunks = isMarkdown
            ? TextChunker.SplitMarkdownParagraphs(lines, MaxTokensPerParagraph, OverlapTokens)
            : TextChunker.SplitPlainTextParagraphs(lines, MaxTokensPerParagraph, OverlapTokens);

        return chunks
            .Select(chunk => chunk.Trim())
            .Where(chunk => !string.IsNullOrWhiteSpace(chunk))
            .ToList();
    }
}
#pragma warning restore SKEXP0050
