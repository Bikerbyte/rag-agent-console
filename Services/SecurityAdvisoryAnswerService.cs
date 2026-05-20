using System.Text;
using CPBLLineBotCloud.Models;
using Microsoft.Extensions.Options;

namespace CPBLLineBotCloud.Services;

public class SecurityAdvisoryAnswerService(
    ISecurityAdvisorySearchService searchService,
    IOptions<SecurityAdvisoryOptions> options) : ISecurityAdvisoryAnswerService
{
    public async Task<string> BuildAnswerAsync(string question, CancellationToken cancellationToken = default)
    {
        var results = await searchService.SearchAsync(question, options.Value.RagMaxChunks, cancellationToken);
        if (results.Count == 0)
        {
            return "目前資料庫裡找不到足夠相關的弱點資料。可以先同步資料，或換成較明確的產品、廠商、CVE ID 再問一次。";
        }

        var builder = new StringBuilder();
        builder.AppendLine("根據目前已同步的弱點資料，整理如下：");

        foreach (var result in results)
        {
            var advisory = result.Advisory;
            builder.AppendLine();
            builder.AppendLine($"- {BuildTitle(advisory)}");
            builder.AppendLine($"  風險: {BuildRiskText(advisory)}");

            var summary = advisory.AiSummary ?? advisory.Description;
            builder.AppendLine($"  摘要: {Trim(summary, 220)}");

            if (!string.IsNullOrWhiteSpace(advisory.SuggestedAction))
            {
                builder.AppendLine($"  建議: {Trim(advisory.SuggestedAction, 180)}");
            }

            builder.AppendLine($"  來源: {advisory.SourceName} {advisory.SourceUrl}");
        }

        return builder.ToString().TrimEnd();
    }

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

    private static string Trim(string value, int maxLength)
    {
        var compact = string.Join(' ', value.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));
        return compact.Length <= maxLength ? compact : compact[..maxLength] + "...";
    }
}
