using System.Globalization;
using System.Text.Json;
using SecurityAdvisoryBot.Models;
using Microsoft.Extensions.Options;

namespace SecurityAdvisoryBot.Services;

public class CisaKevAdvisorySource(
    HttpClient httpClient,
    IOptions<SecurityAdvisoryOptions> options,
    ILogger<CisaKevAdvisorySource> logger) : ISecurityAdvisorySource
{
    public string SourceName => "CISA KEV";

    public async Task<IReadOnlyList<SecurityAdvisoryCandidate>> FetchLatestAsync(CancellationToken cancellationToken = default)
    {
        if (!options.Value.EnableCisaKevSource)
        {
            return [];
        }

        using var response = await httpClient.GetAsync(options.Value.CisaKevJsonUrl, cancellationToken);
        response.EnsureSuccessStatusCode();

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);

        if (!document.RootElement.TryGetProperty("vulnerabilities", out var vulnerabilities) ||
            vulnerabilities.ValueKind != JsonValueKind.Array)
        {
            logger.LogWarning("CISA KEV response did not contain a vulnerabilities array.");
            return [];
        }

        var items = new List<SecurityAdvisoryCandidate>();
        foreach (var item in vulnerabilities.EnumerateArray())
        {
            var cveId = ReadString(item, "cveID");
            if (string.IsNullOrWhiteSpace(cveId))
            {
                continue;
            }

            var title = ReadString(item, "vulnerabilityName") ?? cveId;
            var description = ReadString(item, "shortDescription") ?? title;
            var vendor = ReadString(item, "vendorProject");
            var product = ReadString(item, "product");
            var requiredAction = ReadString(item, "requiredAction");
            var dateAdded = ReadDate(item, "dateAdded");
            var dueDate = ReadDateOnly(item, "dueDate");
            var ransomwareUse = ReadString(item, "knownRansomwareCampaignUse");

            items.Add(new SecurityAdvisoryCandidate(
                SourceName,
                cveId,
                cveId,
                title,
                description,
                vendor,
                product,
                "Known Exploited",
                null,
                true,
                ransomwareUse?.Contains("Known", StringComparison.OrdinalIgnoreCase) == true,
                requiredAction,
                dueDate,
                dateAdded,
                dateAdded,
                "https://www.cisa.gov/known-exploited-vulnerabilities-catalog"));
        }

        return items;
    }

    private static string? ReadString(JsonElement item, string propertyName)
        => item.TryGetProperty(propertyName, out var property) && property.ValueKind == JsonValueKind.String
            ? property.GetString()
            : null;

    private static DateTimeOffset? ReadDate(JsonElement item, string propertyName)
    {
        var value = ReadString(item, propertyName);
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        return DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var parsed)
            ? parsed
            : null;
    }

    private static DateOnly? ReadDateOnly(JsonElement item, string propertyName)
    {
        var value = ReadString(item, propertyName);
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        return DateOnly.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsed)
            ? parsed
            : null;
    }
}

public class NvdAdvisorySource(
    HttpClient httpClient,
    IOptions<SecurityAdvisoryOptions> options,
    ILogger<NvdAdvisorySource> logger) : ISecurityAdvisorySource
{
    public string SourceName => "NVD";

    public async Task<IReadOnlyList<SecurityAdvisoryCandidate>> FetchLatestAsync(CancellationToken cancellationToken = default)
    {
        if (!options.Value.EnableNvdSource)
        {
            return [];
        }

        var lookbackDays = Math.Clamp(options.Value.NvdLookbackDays, 1, 120);
        var maxResults = Math.Clamp(options.Value.MaxNvdResultsPerSync, 1, 2000);
        var end = DateTimeOffset.UtcNow;
        var start = end.AddDays(-lookbackDays);
        var startText = start.ToString("yyyy-MM-dd'T'HH:mm:ss.fff'Z'", CultureInfo.InvariantCulture);
        var endText = end.ToString("yyyy-MM-dd'T'HH:mm:ss.fff'Z'", CultureInfo.InvariantCulture);
        var requestUri = string.Create(
            CultureInfo.InvariantCulture,
            $"/rest/json/cves/2.0?lastModStartDate={Uri.EscapeDataString(startText)}&lastModEndDate={Uri.EscapeDataString(endText)}&resultsPerPage={maxResults}");

        using var response = await httpClient.GetAsync(requestUri, cancellationToken);
        response.EnsureSuccessStatusCode();

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);

        if (!document.RootElement.TryGetProperty("vulnerabilities", out var vulnerabilities) ||
            vulnerabilities.ValueKind != JsonValueKind.Array)
        {
            logger.LogWarning("NVD response did not contain a vulnerabilities array.");
            return [];
        }

        var items = new List<SecurityAdvisoryCandidate>();
        foreach (var vulnerability in vulnerabilities.EnumerateArray())
        {
            if (!vulnerability.TryGetProperty("cve", out var cve))
            {
                continue;
            }

            var cveId = ReadString(cve, "id");
            if (string.IsNullOrWhiteSpace(cveId))
            {
                continue;
            }

            var description = ReadEnglishDescription(cve) ?? cveId;
            var (severity, score) = ReadCvss(cve);
            var published = ReadDate(cve, "published");
            var lastModified = ReadDate(cve, "lastModified");

            items.Add(new SecurityAdvisoryCandidate(
                SourceName,
                cveId,
                cveId,
                cveId,
                description,
                null,
                null,
                severity,
                score,
                false,
                false,
                null,
                null,
                published,
                lastModified,
                $"https://nvd.nist.gov/vuln/detail/{Uri.EscapeDataString(cveId)}"));
        }

        return items;
    }

    private static string? ReadString(JsonElement item, string propertyName)
        => item.TryGetProperty(propertyName, out var property) && property.ValueKind == JsonValueKind.String
            ? property.GetString()
            : null;

    private static DateTimeOffset? ReadDate(JsonElement item, string propertyName)
    {
        var value = ReadString(item, propertyName);
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        return DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var parsed)
            ? parsed
            : null;
    }

    private static string? ReadEnglishDescription(JsonElement cve)
    {
        if (!cve.TryGetProperty("descriptions", out var descriptions) ||
            descriptions.ValueKind != JsonValueKind.Array)
        {
            return null;
        }

        foreach (var description in descriptions.EnumerateArray())
        {
            if (string.Equals(ReadString(description, "lang"), "en", StringComparison.OrdinalIgnoreCase))
            {
                return ReadString(description, "value");
            }
        }

        return null;
    }

    private static (string? Severity, decimal? Score) ReadCvss(JsonElement cve)
    {
        if (!cve.TryGetProperty("metrics", out var metrics))
        {
            return (null, null);
        }

        foreach (var propertyName in new[] { "cvssMetricV40", "cvssMetricV31", "cvssMetricV30", "cvssMetricV2" })
        {
            if (!metrics.TryGetProperty(propertyName, out var metricArray) ||
                metricArray.ValueKind != JsonValueKind.Array)
            {
                continue;
            }

            var metric = metricArray.EnumerateArray().FirstOrDefault();
            if (metric.ValueKind == JsonValueKind.Undefined)
            {
                continue;
            }

            var severity = ReadString(metric, "baseSeverity");
            if (!metric.TryGetProperty("cvssData", out var cvssData))
            {
                return (severity, null);
            }

            decimal? score = null;
            if (cvssData.TryGetProperty("baseScore", out var baseScore) &&
                baseScore.TryGetDecimal(out var parsedScore))
            {
                score = parsedScore;
            }

            severity ??= ReadString(cvssData, "baseSeverity");
            return (severity, score);
        }

        return (null, null);
    }
}
