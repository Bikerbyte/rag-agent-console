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
