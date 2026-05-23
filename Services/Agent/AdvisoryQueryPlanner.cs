using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using SecurityAdvisoryBot.Models;
using Microsoft.Extensions.Options;

namespace SecurityAdvisoryBot.Services;

public partial class AdvisoryQueryPlanner(
    IAiChatClient aiChatClient,
    IOptions<AiProviderOptions> aiProviderOptions,
    ILogger<AdvisoryQueryPlanner> logger) : IAdvisoryQueryPlanner
{
    public async Task<AdvisoryQueryPlan> BuildPlanAsync(
        string question,
        IReadOnlyList<AdvisoryConversationMessage>? history = null,
        CancellationToken cancellationToken = default)
    {
        var options = aiProviderOptions.Value;
        if (options.EnableChatGeneration &&
            !string.Equals(options.Provider, AiProviderNames.Local, StringComparison.OrdinalIgnoreCase))
        {
            var aiPlan = await TryBuildWithAiAsync(question, history, cancellationToken);
            if (aiPlan is not null)
            {
                return aiPlan;
            }
        }

        return BuildLocalPlan(question);
    }

    private async Task<AdvisoryQueryPlan?> TryBuildWithAiAsync(
        string question,
        IReadOnlyList<AdvisoryConversationMessage>? history,
        CancellationToken cancellationToken)
    {
        var systemPrompt = """
        You are a CVE RAG query planner.
        Return JSON only. Do not include markdown fences.
        Extract intent, vendor, product, version, cveId, riskFilter, retrievalQuery, searchKeywords, and notes.
        riskFilter must be one of: known_exploited, critical, high_risk, none.
        Version must be supporting context only; do not include it in searchKeywords unless the advisory context explicitly has version range fields.
        RetrievalQuery should be concise English keywords for vector retrieval.
        """;

        var userPrompt = $$"""
        Conversation history:
        {{BuildHistoryText(history)}}

        User question:
        {{question}}

        Expected JSON shape:
        {
          "intent": "vulnerability_lookup",
          "vendor": "Citrix",
          "product": "NetScaler",
          "version": "59.22",
          "cveId": null,
          "riskFilter": "none",
          "retrievalQuery": "Citrix NetScaler vulnerabilities",
          "searchKeywords": ["citrix", "netscaler"],
          "notes": ["version is supporting context, not a hard retrieval filter"]
        }
        """;

        var response = await aiChatClient.CompleteAsync(systemPrompt, userPrompt, cancellationToken);
        if (string.IsNullOrWhiteSpace(response))
        {
            return null;
        }

        try
        {
            var json = StripJsonFence(response);
            var dto = JsonSerializer.Deserialize<QueryPlanDto>(json, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            });

            if (dto is null)
            {
                return null;
            }

            var keywords = NormalizeKeywords(dto.SearchKeywords, dto.Version);
            var retrievalQuery = string.IsNullOrWhiteSpace(dto.RetrievalQuery)
                ? BuildRetrievalQuery(dto.CveId, keywords, dto.RiskFilter)
                : dto.RetrievalQuery.Trim();

            return new AdvisoryQueryPlan(
                question,
                retrievalQuery,
                NormalizeNullable(dto.Intent),
                NormalizeNullable(dto.Vendor),
                NormalizeNullable(dto.Product),
                NormalizeNullable(dto.Version),
                NormalizeCve(dto.CveId),
                NormalizeRiskFilter(dto.RiskFilter),
                keywords,
                dto.Notes?.Where(note => !string.IsNullOrWhiteSpace(note)).Select(note => note.Trim()).ToList() ?? []);
        }
        catch (JsonException exception)
        {
            logger.LogDebug(exception, "AI query planner returned invalid JSON.");
            return null;
        }
    }

    private static AdvisoryQueryPlan BuildLocalPlan(string question)
    {
        var cveId = NormalizeCve(CveRegex().Match(question).Value);
        var version = ExtractVersion(question);
        var riskFilter = ExtractRiskFilter(question);
        var keywords = ExtractKeywords(question, version);
        var retrievalQuery = BuildRetrievalQuery(cveId, keywords, riskFilter);
        var notes = new List<string>();

        if (!string.IsNullOrWhiteSpace(version))
        {
            notes.Add("version is supporting context, not a hard retrieval filter");
        }

        return new AdvisoryQueryPlan(
            question,
            retrievalQuery,
            "vulnerability_lookup",
            keywords.ElementAtOrDefault(0),
            keywords.ElementAtOrDefault(1),
            version,
            cveId,
            riskFilter,
            keywords,
            notes);
    }

    private static string BuildRetrievalQuery(string? cveId, IReadOnlyList<string> keywords, string? riskFilter)
    {
        if (!string.IsNullOrWhiteSpace(cveId))
        {
            return cveId;
        }

        var builder = new StringBuilder();
        foreach (var keyword in keywords.Take(5))
        {
            builder.Append(keyword).Append(' ');
        }

        if (string.Equals(riskFilter, "known_exploited", StringComparison.OrdinalIgnoreCase))
        {
            builder.Append("known exploited ");
        }
        else if (string.Equals(riskFilter, "critical", StringComparison.OrdinalIgnoreCase) ||
                 string.Equals(riskFilter, "high_risk", StringComparison.OrdinalIgnoreCase))
        {
            builder.Append("critical high risk ");
        }

        builder.Append("vulnerabilities");
        return builder.ToString().Trim();
    }

    private static IReadOnlyList<string> ExtractKeywords(string question, string? version)
    {
        var keywords = new List<string>();
        foreach (Match match in KeywordRegex().Matches(question.ToLowerInvariant()))
        {
            var keyword = match.Value.Trim('.', ':', '-');
            if (string.IsNullOrWhiteSpace(keyword) ||
                StopWords.Contains(keyword) ||
                IsVersionKeyword(keyword, version) ||
                CveRegex().IsMatch(keyword))
            {
                continue;
            }

            keywords.Add(keyword);
        }

        return NormalizeKeywords(keywords, version);
    }

    private static IReadOnlyList<string> NormalizeKeywords(IEnumerable<string>? keywords, string? version)
        => (keywords ?? [])
            .Select(keyword => keyword.Trim().Trim('.', ':', '-').ToLowerInvariant())
            .Where(keyword => !string.IsNullOrWhiteSpace(keyword))
            .Where(keyword => !StopWords.Contains(keyword))
            .Where(keyword => !IsVersionKeyword(keyword, version))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(8)
            .ToList();

    private static string? ExtractVersion(string question)
    {
        var match = VersionRegex().Match(question);
        return match.Success ? match.Value : null;
    }

    private static string? ExtractRiskFilter(string question)
    {
        if (ContainsAny(question, "kev", "known exploited", "已知遭利用", "被利用"))
        {
            return "known_exploited";
        }

        if (ContainsAny(question, "critical", "嚴重"))
        {
            return "critical";
        }

        if (ContainsAny(question, "high risk", "高風險", "cvss"))
        {
            return "high_risk";
        }

        return "none";
    }

    private static string BuildHistoryText(IReadOnlyList<AdvisoryConversationMessage>? history)
    {
        if (history is null || history.Count == 0)
        {
            return "(none)";
        }

        return string.Join(Environment.NewLine, history.TakeLast(6).Select(item => $"{item.Role}: {item.Content}"));
    }

    private static bool IsVersionKeyword(string keyword, string? version)
        => (!string.IsNullOrWhiteSpace(version) && string.Equals(keyword, version, StringComparison.OrdinalIgnoreCase)) ||
           VersionRegex().IsMatch(keyword);

    private static bool ContainsAny(string value, params string[] tokens)
        => tokens.Any(token => value.Contains(token, StringComparison.OrdinalIgnoreCase));

    private static string StripJsonFence(string value)
    {
        var trimmed = value.Trim();
        if (trimmed.StartsWith("```", StringComparison.Ordinal))
        {
            trimmed = Regex.Replace(trimmed, "^```(?:json)?\\s*", string.Empty, RegexOptions.IgnoreCase);
            trimmed = Regex.Replace(trimmed, "\\s*```$", string.Empty);
        }

        return trimmed.Trim();
    }

    private static string? NormalizeNullable(string? value)
        => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static string? NormalizeCve(string? value)
        => string.IsNullOrWhiteSpace(value) ? null : value.Trim().ToUpperInvariant();

    private static string NormalizeRiskFilter(string? value)
        => string.IsNullOrWhiteSpace(value) ? "none" : value.Trim().ToLowerInvariant();

    private static readonly HashSet<string> StopWords = new(StringComparer.OrdinalIgnoreCase)
    {
        "ask", "help", "latest", "recent", "today", "this", "week", "list", "show",
        "risk", "high", "critical", "kev", "cve", "sync", "watch", "follow", "version",
        "has", "have", "with", "about", "vulnerability", "vulnerabilities", "known",
        "exploited", "版本", "弱點", "漏洞", "有", "嗎", "哪些", "最近"
    };

    [GeneratedRegex("cve-\\d{4}-\\d{4,}", RegexOptions.IgnoreCase)]
    private static partial Regex CveRegex();

    [GeneratedRegex("(?<!cve-)\\b\\d+(?:\\.\\d+){1,3}[a-z0-9-]*\\b", RegexOptions.IgnoreCase)]
    private static partial Regex VersionRegex();

    [GeneratedRegex("[a-z0-9_.:-]{2,}")]
    private static partial Regex KeywordRegex();

    private sealed record QueryPlanDto(
        string? Intent,
        string? Vendor,
        string? Product,
        string? Version,
        string? CveId,
        string? RiskFilter,
        string? RetrievalQuery,
        IReadOnlyList<string>? SearchKeywords,
        IReadOnlyList<string>? Notes);
}
