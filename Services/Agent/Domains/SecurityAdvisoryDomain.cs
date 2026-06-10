using System.Text;
using System.Text.RegularExpressions;
using RagAgentConsole.Models;

namespace RagAgentConsole.Services;

/// <summary>
/// Plan/filter keys interpreted only by the security advisory domain.
/// The generic pipeline carries them as opaque entity/filter values.
/// </summary>
public static class SecurityAdvisoryPlanKeys
{
    public const string CveId = "cveId";
    public const string RiskFilter = "riskFilter";
    public const string CveYear = "cveYear";
}

/// <summary>Trace metadata keys emitted by the security advisory domain.</summary>
public static class SecurityAdvisoryTraceKeys
{
    public const string CveId = SecurityAdvisoryPlanKeys.CveId;
    public const string Severity = "severity";
    public const string CvssScore = "cvssScore";
    public const string KnownExploited = "knownExploited";
}

/// <summary>
/// Typed view over the security-specific entity/filter values carried by a
/// retrieval request. Keeps dictionary parsing in one place so vector stores
/// and the text scorer don't interpret raw keys themselves.
/// </summary>
public sealed record SecurityAdvisoryFilter(
    string? CveId,
    string? Version,
    bool KevOnly,
    bool HighRiskOnly,
    int? CveYear)
{
    public const string RiskKnownExploited = "known_exploited";
    public const string RiskCritical = "critical";
    public const string RiskHigh = "high_risk";

    public static SecurityAdvisoryFilter From(RetrievalRequest request)
        => From(request.GetEntity(SecurityAdvisoryPlanKeys.CveId),
            request.GetEntity(PlanEntityKeys.Version),
            request.GetFilter(SecurityAdvisoryPlanKeys.RiskFilter),
            request.GetFilter(SecurityAdvisoryPlanKeys.CveYear));

    public static SecurityAdvisoryFilter From(RetrievalPlan plan)
        => From(plan.GetEntity(SecurityAdvisoryPlanKeys.CveId),
            plan.GetEntity(PlanEntityKeys.Version),
            plan.GetFilter(SecurityAdvisoryPlanKeys.RiskFilter),
            plan.GetFilter(SecurityAdvisoryPlanKeys.CveYear));

    private static SecurityAdvisoryFilter From(string? cveId, string? version, string? riskFilter, string? cveYear)
        => new(
            string.IsNullOrWhiteSpace(cveId) ? null : cveId,
            string.IsNullOrWhiteSpace(version) ? null : version,
            string.Equals(riskFilter, RiskKnownExploited, StringComparison.OrdinalIgnoreCase),
            string.Equals(riskFilter, RiskCritical, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(riskFilter, RiskHigh, StringComparison.OrdinalIgnoreCase),
            int.TryParse(cveYear, out var year) ? year : null);
}

/// <summary>
/// Built-in demo domain for the CISA KEV / NVD security advisory connector.
/// All CVE-specific normalization, context formatting, and trace metadata
/// live here instead of in the generic RAG pipeline.
/// </summary>
public sealed partial class SecurityAdvisoryDomain : IRagDomain
{
    public const string DomainName = "security_advisory";

    public string Name => DomainName;

    public string DefaultModuleName => KnowledgeModuleNames.CveAdvisory;

    public IReadOnlyList<string> ModuleNames { get; } = [KnowledgeModuleNames.CveAdvisory];

    public bool Owns(RetrievalResult result) => result.Advisory is not null;

    public RetrievalPlan NormalizePlan(RetrievalPlan plan, string question)
    {
        var entities = new Dictionary<string, string?>(plan.Entities ?? RetrievalPlan.EmptyValues);
        var filters = new Dictionary<string, string?>(plan.Filters ?? RetrievalPlan.EmptyValues);

        // CVE id: prefer planner output, otherwise deterministic regex
        // extraction from the question; always uppercase.
        var cveId = Normalize(GetValue(entities, SecurityAdvisoryPlanKeys.CveId));
        if (cveId is null)
        {
            var match = CveIdRegex().Match(question);
            cveId = match.Success ? match.Value : null;
        }

        if (cveId is not null)
        {
            entities[SecurityAdvisoryPlanKeys.CveId] = cveId.ToUpperInvariant();
        }
        else
        {
            entities.Remove(SecurityAdvisoryPlanKeys.CveId);
        }

        // Risk filter: enum validation; drop everything except known values.
        var riskFilter = Normalize(GetValue(filters, SecurityAdvisoryPlanKeys.RiskFilter))?.ToLowerInvariant();
        if (riskFilter is SecurityAdvisoryFilter.RiskKnownExploited
            or SecurityAdvisoryFilter.RiskCritical
            or SecurityAdvisoryFilter.RiskHigh)
        {
            filters[SecurityAdvisoryPlanKeys.RiskFilter] = riskFilter;
        }
        else
        {
            filters.Remove(SecurityAdvisoryPlanKeys.RiskFilter);
        }

        // CVE year: numeric or absent.
        if (!int.TryParse(GetValue(filters, SecurityAdvisoryPlanKeys.CveYear), out _))
        {
            filters.Remove(SecurityAdvisoryPlanKeys.CveYear);
        }

        return plan with
        {
            RetrievalQuery = string.IsNullOrWhiteSpace(plan.RetrievalQuery)
                ? cveId?.ToUpperInvariant() ?? question.Trim()
                : plan.RetrievalQuery.Trim(),
            Entities = entities,
            Filters = filters
        };
    }

    public string BuildContextBlock(RetrievalResult result)
    {
        if (result.Advisory is not { } advisory)
        {
            // Managed documents routed to the CveAdvisory module carry no
            // advisory metadata; format them as generic knowledge instead.
            return GenericFallback.BuildContextBlock(result);
        }

        var builder = new StringBuilder();
        builder.AppendLine($"Module: {result.ModuleName}");
        builder.AppendLine($"Source kind: {result.SourceKind}");
        builder.AppendLine($"CVE: {advisory.CveId ?? advisory.ExternalId}");
        builder.AppendLine($"Title: {advisory.Title}");
        builder.AppendLine($"Vendor: {advisory.Vendor}");
        builder.AppendLine($"Product: {advisory.Product}");
        builder.AppendLine($"Severity: {advisory.Severity}");
        builder.AppendLine($"CVSS: {advisory.CvssScore}");
        builder.AppendLine($"Known exploited: {advisory.IsKnownExploited}");
        builder.AppendLine($"Published at: {advisory.PublishedAt:yyyy-MM-dd}");
        builder.AppendLine($"Last modified at: {advisory.LastModifiedAt:yyyy-MM-dd}");
        builder.AppendLine($"Context chunk: {result.ChunkText}");
        builder.AppendLine($"Source: {advisory.SourceName} {advisory.SourceUrl}");
        return builder.ToString();
    }

    public string BuildPlainSummaryBlock(RetrievalResult result)
    {
        if (result.Advisory is not { } advisory)
        {
            return GenericFallback.BuildPlainSummaryBlock(result);
        }

        var builder = new StringBuilder();
        builder.AppendLine($"- {BuildTitle(advisory)}");
        builder.AppendLine($"  狀態: {BuildRiskText(advisory)}");
        builder.AppendLine($"  摘要: {TextBlockHelper.Compact(advisory.AiSummary ?? advisory.Description, 220)}");
        if (!string.IsNullOrWhiteSpace(advisory.SuggestedAction))
        {
            builder.AppendLine($"  建議: {TextBlockHelper.Compact(advisory.SuggestedAction, 180)}");
        }

        builder.Append($"  來源: {advisory.SourceName} {advisory.SourceUrl}");
        return builder.ToString();
    }

    public IReadOnlyDictionary<string, string?> BuildTraceMetadata(RetrievalResult result)
    {
        if (result.Advisory is not { } advisory)
        {
            return GenericFallback.BuildTraceMetadata(result);
        }

        return new Dictionary<string, string?>
        {
            [SecurityAdvisoryTraceKeys.CveId] = advisory.CveId ?? advisory.ExternalId,
            [PlanEntityKeys.Vendor] = advisory.Vendor,
            [PlanEntityKeys.Product] = advisory.Product,
            [SecurityAdvisoryTraceKeys.Severity] = advisory.Severity,
            [SecurityAdvisoryTraceKeys.CvssScore] = advisory.CvssScore?.ToString("0.0"),
            [SecurityAdvisoryTraceKeys.KnownExploited] = advisory.IsKnownExploited ? "true" : "false"
        };
    }

    private static readonly GenericKnowledgeDomain GenericFallback = new();

    private static string BuildTitle(SecurityAdvisory advisory)
        => string.IsNullOrWhiteSpace(advisory.CveId)
            ? advisory.Title
            : $"{advisory.CveId} - {advisory.Title}";

    private static string BuildRiskText(SecurityAdvisory advisory)
    {
        var parts = new List<string>();
        if (!string.IsNullOrWhiteSpace(advisory.Severity))
        {
            parts.Add(advisory.Severity);
        }

        if (advisory.CvssScore.HasValue)
        {
            parts.Add($"CVSS {advisory.CvssScore:0.0}");
        }

        if (advisory.IsKnownExploited)
        {
            parts.Add("known exploited");
        }

        if (advisory.HasRansomwareUse)
        {
            parts.Add("ransomware use");
        }

        return parts.Count == 0 ? "未標示" : string.Join(" / ", parts);
    }

    private static string? GetValue(Dictionary<string, string?> values, string key)
        => values.TryGetValue(key, out var value) ? value : null;

    private static string? Normalize(string? value)
        => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    [GeneratedRegex(@"\bCVE-\d{4}-\d{4,}\b", RegexOptions.IgnoreCase)]
    private static partial Regex CveIdRegex();
}
