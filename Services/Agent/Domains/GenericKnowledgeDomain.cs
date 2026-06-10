using System.Text;
using RagAgentConsole.Models;

namespace RagAgentConsole.Services;

/// <summary>
/// Default domain for uploaded internal documents (SOP, HR policy, FAQ, …).
/// Formats context with generic document metadata only — no CVE assumptions.
/// </summary>
public sealed class GenericKnowledgeDomain : IRagDomain
{
    public const string DomainName = "generic_knowledge";

    public string Name => DomainName;

    public string DefaultModuleName => KnowledgeModuleNames.InternalDocs;

    public IReadOnlyList<string> ModuleNames { get; } =
        [KnowledgeModuleNames.WorkflowQa, KnowledgeModuleNames.InternalDocs];

    public bool Owns(RetrievalResult result) => result.Advisory is null;

    public RetrievalPlan NormalizePlan(RetrievalPlan plan, string question)
        => plan with
        {
            RetrievalQuery = string.IsNullOrWhiteSpace(plan.RetrievalQuery)
                ? question.Trim()
                : plan.RetrievalQuery.Trim()
        };

    public string BuildContextBlock(RetrievalResult result)
    {
        var builder = new StringBuilder();
        builder.AppendLine($"Module: {result.ModuleName}");
        builder.AppendLine($"Source kind: {result.SourceKind}");
        builder.AppendLine($"Title: {result.Title}");
        AppendIfPresent(builder, "Vendor", result.Document?.Vendor);
        AppendIfPresent(builder, "Product", result.Document?.Product);
        AppendIfPresent(builder, "Tags", result.Document?.Tags);
        builder.AppendLine($"Context chunk: {result.ChunkText}");
        builder.AppendLine($"Source: {result.SourceName}");
        return builder.ToString();
    }

    public string BuildPlainSummaryBlock(RetrievalResult result)
    {
        var builder = new StringBuilder();
        builder.AppendLine($"- {result.Title}");
        builder.AppendLine($"  模組: {result.ModuleName}");
        builder.AppendLine($"  摘要: {TextBlockHelper.Compact(result.ChunkText, 220)}");
        builder.Append($"  來源: {result.SourceName}");
        return builder.ToString();
    }

    public IReadOnlyDictionary<string, string?> BuildTraceMetadata(RetrievalResult result)
    {
        var metadata = new Dictionary<string, string?>();
        AddIfPresent(metadata, PlanEntityKeys.Vendor, result.Document?.Vendor);
        AddIfPresent(metadata, PlanEntityKeys.Product, result.Document?.Product);
        return metadata;
    }

    private static void AppendIfPresent(StringBuilder builder, string label, string? value)
    {
        if (!string.IsNullOrWhiteSpace(value))
        {
            builder.AppendLine($"{label}: {value}");
        }
    }

    private static void AddIfPresent(Dictionary<string, string?> metadata, string key, string? value)
    {
        if (!string.IsNullOrWhiteSpace(value))
        {
            metadata[key] = value;
        }
    }
}

internal static class TextBlockHelper
{
    public static string Compact(string value, int maxLength)
    {
        var compact = string.Join(' ', value.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));
        return compact.Length <= maxLength ? compact : compact[..maxLength] + "...";
    }
}
