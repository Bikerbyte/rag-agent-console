using System.Globalization;
using System.Text;
using CsvHelper;
using CsvHelper.Configuration;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;
using HtmlAgilityPack;
using Markdig;
using RagAgentConsole.Models;

namespace RagAgentConsole.Services;

public interface IKnowledgeDocumentTextExtractor
{
    Task<KnowledgeExtractedText> ExtractAsync(
        string fileName,
        string? contentType,
        Stream contentStream,
        CancellationToken cancellationToken = default);
}

public class KnowledgeDocumentTextExtractor : IKnowledgeDocumentTextExtractor
{
    private static readonly MarkdownPipeline MarkdownPipeline = new MarkdownPipelineBuilder()
        .UseAdvancedExtensions()
        .Build();

    public async Task<KnowledgeExtractedText> ExtractAsync(
        string fileName,
        string? contentType,
        Stream contentStream,
        CancellationToken cancellationToken = default)
    {
        var extension = Path.GetExtension(fileName).ToLowerInvariant();
        contentStream.Position = 0;

        return extension switch
        {
            ".md" or ".markdown" => await ExtractMarkdownAsync(contentStream, cancellationToken),
            ".txt" or ".log" => await ExtractPlainTextAsync(contentStream, cancellationToken),
            ".html" or ".htm" => await ExtractHtmlAsync(contentStream, cancellationToken),
            ".csv" => await ExtractCsvAsync(contentStream, cancellationToken),
            ".docx" => ExtractDocx(contentStream),
            _ => throw new NotSupportedException($"File type {extension} is not supported yet.")
        };
    }

    private static async Task<KnowledgeExtractedText> ExtractPlainTextAsync(
        Stream contentStream,
        CancellationToken cancellationToken)
    {
        using var reader = new StreamReader(contentStream, Encoding.UTF8, detectEncodingFromByteOrderMarks: true, leaveOpen: true);
        var text = await reader.ReadToEndAsync(cancellationToken);
        return new KnowledgeExtractedText(NormalizeText(text), "PlainText", "text/plain");
    }

    private static async Task<KnowledgeExtractedText> ExtractMarkdownAsync(
        Stream contentStream,
        CancellationToken cancellationToken)
    {
        using var reader = new StreamReader(contentStream, Encoding.UTF8, detectEncodingFromByteOrderMarks: true, leaveOpen: true);
        var markdown = await reader.ReadToEndAsync(cancellationToken);
        var html = Markdown.ToHtml(markdown, MarkdownPipeline);
        var text = ExtractTextFromHtml(html);
        return new KnowledgeExtractedText(NormalizeText(text), "Markdig", "text/markdown");
    }

    private static async Task<KnowledgeExtractedText> ExtractHtmlAsync(
        Stream contentStream,
        CancellationToken cancellationToken)
    {
        using var reader = new StreamReader(contentStream, Encoding.UTF8, detectEncodingFromByteOrderMarks: true, leaveOpen: true);
        var html = await reader.ReadToEndAsync(cancellationToken);
        return new KnowledgeExtractedText(NormalizeText(ExtractTextFromHtml(html)), "HtmlAgilityPack", "text/html");
    }

    private static async Task<KnowledgeExtractedText> ExtractCsvAsync(
        Stream contentStream,
        CancellationToken cancellationToken)
    {
        using var reader = new StreamReader(contentStream, Encoding.UTF8, detectEncodingFromByteOrderMarks: true, leaveOpen: true);
        using var csv = new CsvReader(reader, new CsvConfiguration(CultureInfo.InvariantCulture)
        {
            BadDataFound = null,
            MissingFieldFound = null
        });

        var builder = new StringBuilder();
        while (await csv.ReadAsync())
        {
            cancellationToken.ThrowIfCancellationRequested();
            var fields = csv.Parser.Record?
                .Where(field => !string.IsNullOrWhiteSpace(field))
                .Select(field => field.Trim())
                .ToList() ?? [];

            if (fields.Count > 0)
            {
                builder.AppendLine(string.Join(" | ", fields));
            }
        }

        return new KnowledgeExtractedText(NormalizeText(builder.ToString()), "CsvHelper", "text/csv");
    }

    private static KnowledgeExtractedText ExtractDocx(Stream contentStream)
    {
        using var document = WordprocessingDocument.Open(contentStream, false);
        var body = document.MainDocumentPart?.Document?.Body;
        if (body is null)
        {
            return new KnowledgeExtractedText(string.Empty, "DocumentFormat.OpenXml", "application/vnd.openxmlformats-officedocument.wordprocessingml.document");
        }

        var paragraphs = body
            .Descendants<Paragraph>()
            .Select(paragraph => string.Concat(paragraph.Descendants<Text>().Select(text => text.Text)))
            .Where(text => !string.IsNullOrWhiteSpace(text));

        return new KnowledgeExtractedText(
            NormalizeText(string.Join(Environment.NewLine, paragraphs)),
            "DocumentFormat.OpenXml",
            "application/vnd.openxmlformats-officedocument.wordprocessingml.document");
    }

    private static string ExtractTextFromHtml(string html)
    {
        var document = new HtmlDocument();
        document.LoadHtml(html);
        var body = document.DocumentNode.SelectSingleNode("//body") ?? document.DocumentNode;
        return HtmlEntity.DeEntitize(body.InnerText);
    }

    private static string NormalizeText(string text)
    {
        var lines = text
            .Replace("\r\n", "\n")
            .Replace('\r', '\n')
            .Split('\n', StringSplitOptions.TrimEntries)
            .Where(line => !string.IsNullOrWhiteSpace(line));

        return string.Join(Environment.NewLine, lines);
    }
}
