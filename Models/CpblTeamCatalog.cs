namespace CPBLLineBotCloud.Models;

public static class CpblTeamCatalog
{
    private static readonly IReadOnlyDictionary<string, TeamDisplayEntry> Teams =
        new Dictionary<string, TeamDisplayEntry>(StringComparer.OrdinalIgnoreCase)
        {
            ["FG"] = new("富邦悍將", "Fubon Guardians"),
            ["CT"] = new("中信兄弟", "CTBC Brothers"),
            ["UL"] = new("統一7-ELEVEn獅", "Uni-Lions"),
            ["RA"] = new("樂天桃猿", "Rakuten Monkeys"),
            ["WD"] = new("味全龍", "Wei Chuan Dragons"),
            ["TS"] = new("台鋼雄鷹", "TSG Hawks")
        };

    private static readonly IReadOnlyDictionary<string, string> Aliases =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["富邦"] = "FG",
            ["邦邦"] = "FG",
            ["悍將"] = "FG",
            ["富邦悍將"] = "FG",
            ["fubon"] = "FG",
            ["fubon guardians"] = "FG",
            ["中信"] = "CT",
            ["爪"] = "CT",
            ["爪爪"] = "CT",
            ["中信兄弟"] = "CT",
            ["兄弟"] = "CT",
            ["ctbc"] = "CT",
            ["ctbc brothers"] = "CT",
            ["統一"] = "UL",
            ["喵"] = "UL",
            ["獅"] = "UL",
            ["統一獅"] = "UL",
            ["統一7-eleven獅"] = "UL",
            ["統一7-eleven狮"] = "UL",
            ["uni-lions"] = "UL",
            ["樂天"] = "RA",
            ["吱"] = "RA",
            ["猴"] = "RA",
            ["樂天桃猿"] = "RA",
            ["rakuten"] = "RA",
            ["rakuten monkeys"] = "RA",
            ["味全"] = "WD",
            ["龍"] = "WD",
            ["味全龍"] = "WD",
            ["wei chuan dragons"] = "WD",
            ["台鋼"] = "TS",
            ["鷹"] = "TS",
            ["台鋼雄鷹"] = "TS",
            ["tsg hawks"] = "TS"
        };

    public static string GetDisplayName(string? teamCode)
    {
        if (string.IsNullOrWhiteSpace(teamCode))
        {
            return "未知球隊";
        }

        return Teams.TryGetValue(teamCode, out var entry)
            ? entry.DisplayName
            : teamCode;
    }

    public static string GetEnglishName(string? teamCode)
    {
        if (string.IsNullOrWhiteSpace(teamCode))
        {
            return "Unknown Team";
        }

        return Teams.TryGetValue(teamCode, out var entry)
            ? entry.EnglishName
            : teamCode;
    }

    public static bool TryResolveTeamCode(string? rawValue, out string teamCode)
    {
        teamCode = string.Empty;

        if (string.IsNullOrWhiteSpace(rawValue))
        {
            return false;
        }

        var normalized = rawValue.Trim();

        if (Teams.ContainsKey(normalized))
        {
            teamCode = normalized.ToUpperInvariant();
            return true;
        }

        if (Aliases.TryGetValue(normalized, out var resolvedTeamCode))
        {
            teamCode = resolvedTeamCode;
            return true;
        }

        var normalizedLookup = NormalizeLookupValue(normalized);

        foreach (var team in Teams)
        {
            if (string.Equals(NormalizeLookupValue(team.Key), normalizedLookup, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(NormalizeLookupValue(team.Value.DisplayName), normalizedLookup, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(NormalizeLookupValue(team.Value.EnglishName), normalizedLookup, StringComparison.OrdinalIgnoreCase))
            {
                teamCode = team.Key;
                return true;
            }
        }

        foreach (var alias in Aliases)
        {
            if (string.Equals(NormalizeLookupValue(alias.Key), normalizedLookup, StringComparison.OrdinalIgnoreCase))
            {
                teamCode = alias.Value;
                return true;
            }
        }

        return false;
    }

    public static bool TryResolveTeamCodeFromText(string? rawValue, out string teamCode, out string matchedText)
    {
        teamCode = string.Empty;
        matchedText = string.Empty;

        if (string.IsNullOrWhiteSpace(rawValue))
        {
            return false;
        }

        var normalizedInput = NormalizeLookupValue(rawValue);

        foreach (var team in Teams.OrderByDescending(item => item.Value.DisplayName.Length))
        {
            if (rawValue.Contains(team.Value.DisplayName, StringComparison.OrdinalIgnoreCase) ||
                normalizedInput.Contains(NormalizeLookupValue(team.Value.DisplayName), StringComparison.OrdinalIgnoreCase) ||
                normalizedInput.Contains(NormalizeLookupValue(team.Value.EnglishName), StringComparison.OrdinalIgnoreCase))
            {
                teamCode = team.Key;
                matchedText = team.Value.DisplayName;
                return true;
            }
        }

        foreach (var alias in Aliases.OrderByDescending(item => item.Key.Length))
        {
            if (rawValue.Contains(alias.Key, StringComparison.OrdinalIgnoreCase) ||
                normalizedInput.Contains(NormalizeLookupValue(alias.Key), StringComparison.OrdinalIgnoreCase))
            {
                teamCode = alias.Value;
                matchedText = alias.Key;
                return true;
            }
        }

        return false;
    }

    public static IReadOnlyList<string> GetSearchKeywords(string teamCode)
    {
        return teamCode.ToUpperInvariant() switch
        {
            "FG" => ["富邦", "悍將", "邦邦"],
            "CT" => ["中信", "兄弟", "爪"],
            "UL" => ["統一", "獅", "喵"],
            "RA" => ["樂天", "桃猿", "吱"],
            "WD" => ["味全", "龍"],
            "TS" => ["台鋼", "雄鷹", "鷹"],
            _ => [GetDisplayName(teamCode)]
        };
    }

    private static string NormalizeLookupValue(string rawValue)
    {
        return rawValue
            .Trim()
            .Replace(" ", string.Empty, StringComparison.Ordinal)
            .Replace("　", string.Empty, StringComparison.Ordinal)
            .Replace("-", string.Empty, StringComparison.Ordinal)
            .Replace("－", string.Empty, StringComparison.Ordinal)
            .Replace("_", string.Empty, StringComparison.Ordinal)
            .Replace("隊", string.Empty, StringComparison.Ordinal)
            .Replace("7-eleven", "7eleven", StringComparison.OrdinalIgnoreCase)
            .ToLowerInvariant();
    }

    private sealed record TeamDisplayEntry(string DisplayName, string EnglishName);
}
