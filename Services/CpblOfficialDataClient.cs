using System.Globalization;
using System.Net;
using System.Text.Json;
using System.Text.RegularExpressions;
using CPBLLineBotCloud.Models;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;

namespace CPBLLineBotCloud.Services;

/// <summary>
/// 負責讀取 CPBL 官方頁面與 AJAX endpoint，並整理成 app 內比較好用的 DTO。
/// </summary>
public partial class CpblOfficialDataClient(
    IHttpClientFactory httpClientFactory,
    IMemoryCache memoryCache,
    IOptions<DataSourceOptions> dataSourceOptions,
    ILogger<CpblOfficialDataClient> logger) : ICpblOfficialDataClient
{
    private readonly DataSourceOptions dataSourceOptions = dataSourceOptions.Value;
    private static readonly string DefaultOfficialBaseUrl = "https://www.cpbl.com.tw";

    public Task<IReadOnlyList<CpblOfficialGameSnapshot>> GetGamesAsync(DateOnly targetDate, CancellationToken cancellationToken = default)
    {
        return GetCachedGamesAsync(targetDate, cancellationToken);
    }

    public async Task<IReadOnlyList<CpblTeamStandingSnapshot>> GetStandingsAsync(CancellationToken cancellationToken = default)
    {
        var standings = await memoryCache.GetOrCreateAsync(
            "cpbl-home-standings",
            async cacheEntry =>
            {
                // 排名資料不用秒級更新，這裡稍微 cache 一下，指令回覆會順很多。
                cacheEntry.AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(10);
                return await GetStandingsCoreAsync(cancellationToken);
            });

        return standings ?? [];
    }

    public async Task<CpblPlayerStatsResult?> GetPlayerStatsAsync(string playerName, CancellationToken cancellationToken = default)
    {
        var player = await FindPlayerAsync(playerName, cancellationToken);
        if (player is null)
        {
            return null;
        }

        return await memoryCache.GetOrCreateAsync(
            $"cpbl-player-stats:{player.AccountId}",
            async cacheEntry =>
            {
                cacheEntry.AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(20);
                return await GetPlayerStatsCoreAsync(player.AccountId, cancellationToken);
            });
    }

    public async Task<CpblMatchupResult?> GetMatchupAsync(string hitterName, string pitcherName, CancellationToken cancellationToken = default)
    {
        var hitter = await FindPlayerAsync(hitterName, cancellationToken);
        var pitcher = await FindPlayerAsync(pitcherName, cancellationToken);

        if (hitter is null || pitcher is null)
        {
            return null;
        }

        var hitterProfile = await GetPlayerProfileAsync(hitter.AccountId, cancellationToken);
        var pitcherProfile = await GetPlayerProfileAsync(pitcher.AccountId, cancellationToken);
        var page = await GetHtmlAsync(BuildFightingPageUrl(hitter.AccountId), cancellationToken);

        var optionsToken = ExtractAjaxToken(page, "getfightingoptsaction");
        var scoreToken = ExtractAjaxToken(page, "getfightingscore");

        if (string.IsNullOrWhiteSpace(optionsToken) || string.IsNullOrWhiteSpace(scoreToken))
        {
            logger.LogWarning("Unable to locate official CPBL matchup tokens for hitter {HitterAcnt}.", hitter.AccountId);
            return null;
        }

        var referer = BuildFightingPageUrl(hitter.AccountId);
        var initialOptions = await PostJsonAsync(
            "/team/getfightingoptsaction",
            new Dictionary<string, string>
            {
                ["acnt"] = hitter.AccountId,
                ["kindCode"] = "A",
                ["year"] = string.Empty,
                ["fightingTeamNo"] = string.Empty,
                ["fightingAcnt"] = string.Empty
            },
            optionsToken,
            referer,
            cancellationToken);

        var year = GetJsonString(initialOptions.RootElement, "Year");
        var teamOptions = ParseOptionList(GetJsonString(initialOptions.RootElement, "FightingTeamOpts"));
        var teamOption = FindTeamOption(teamOptions, pitcherProfile?.TeamName);

        if (teamOption is null)
        {
            return null;
        }

        var opponentOptionsDocument = await PostJsonAsync(
            "/team/getfightingoptsaction",
            new Dictionary<string, string>
            {
                ["acnt"] = hitter.AccountId,
                ["kindCode"] = "A",
                ["year"] = year ?? string.Empty,
                ["fightingTeamNo"] = teamOption.Value,
                ["fightingAcnt"] = string.Empty
            },
            optionsToken,
            referer,
            cancellationToken);

        var opponentOptions = ParseOptionList(GetJsonString(opponentOptionsDocument.RootElement, "FightingAcntOpts"));
        var pitcherOption = opponentOptions.FirstOrDefault(
            option => string.Equals(NormalizeName(option.Text), NormalizeName(pitcher.Name), StringComparison.Ordinal));

        if (pitcherOption is null)
        {
            return null;
        }

        var scoreDocument = await PostJsonAsync(
            "/team/getfightingscore",
            new Dictionary<string, string>
            {
                ["acnt"] = hitter.AccountId,
                ["kindCode"] = "A",
                ["year"] = GetJsonString(opponentOptionsDocument.RootElement, "Year") ?? year ?? string.Empty,
                ["fightingTeamNo"] = teamOption.Value,
                ["fightingAcnt"] = pitcherOption.Value
            },
            scoreToken,
            referer,
            cancellationToken);

        var scoreJson = GetJsonString(scoreDocument.RootElement, "FightingScore");
        if (string.IsNullOrWhiteSpace(scoreJson))
        {
            return null;
        }

        using var scorePayload = JsonDocument.Parse(scoreJson);
        var firstRow = scorePayload.RootElement.EnumerateArray().FirstOrDefault();

        if (firstRow.ValueKind == JsonValueKind.Undefined)
        {
            return null;
        }

        return new CpblMatchupResult
        {
            HitterName = DecodeHtmlText(GetJsonString(firstRow, "HitterName")) ?? hitter.Name,
            PitcherName = DecodeHtmlText(GetJsonString(firstRow, "PitcherName")) ?? pitcher.Name,
            HitterTeamName = DecodeHtmlText(GetJsonString(firstRow, "HitterTeamName")) ?? hitterProfile?.TeamName,
            PitcherTeamName = DecodeHtmlText(GetJsonString(firstRow, "PitcherTeamName")) ?? pitcherProfile?.TeamName,
            PlateAppearances = GetJsonInt(firstRow, "PlateAppearances"),
            Hits = GetJsonInt(firstRow, "HittingCnt"),
            HomeRuns = GetJsonInt(firstRow, "HomeRunCnt"),
            RunsBattedIn = GetJsonInt(firstRow, "RunBattedINCnt"),
            Walks = GetJsonInt(firstRow, "BasesONBallsCnt"),
            Strikeouts = GetJsonInt(firstRow, "StrikeOutCnt"),
            Average = GetJsonDecimal(firstRow, "Avg"),
            OnBasePercentage = GetJsonDecimal(firstRow, "Obp"),
            SluggingPercentage = GetJsonDecimal(firstRow, "Slg"),
            Ops = GetJsonDecimal(firstRow, "Ops")
        };
    }

    private async Task<IReadOnlyList<CpblOfficialGameSnapshot>> GetGamesCoreAsync(DateOnly targetDate, CancellationToken cancellationToken)
    {
        // 官方 detail endpoint 依賴首頁當下發出的 token，先抓首頁再打 API。
        var homePageHtml = await GetHtmlAsync(GetCandidateBaseUrls(), cancellationToken);
        var antiForgeryToken = ExtractHomePageToken(homePageHtml);

        if (string.IsNullOrWhiteSpace(antiForgeryToken))
        {
            throw new InvalidOperationException("Unable to find the official CPBL anti-forgery token.");
        }

        using var payload = await PostJsonAsync(
            "/home/getdetaillist",
            new Dictionary<string, string>
            {
                ["__RequestVerificationToken"] = antiForgeryToken,
                ["GameDate"] = targetDate.ToString("yyyy/MM/dd", CultureInfo.InvariantCulture),
                ["GameSno"] = string.Empty,
                ["KindCode"] = string.Empty
            },
            null,
            GetCandidateBaseUrls(),
            cancellationToken);

        var gameJson = GetJsonString(payload.RootElement, "GameADetailJson");
        if (string.IsNullOrWhiteSpace(gameJson))
        {
            return [];
        }

        using var gameDocument = JsonDocument.Parse(gameJson);
        var results = new List<CpblOfficialGameSnapshot>();

        foreach (var gameElement in gameDocument.RootElement.EnumerateArray())
        {
            var localDateTime = ParseLocalGameTime(gameElement, targetDate);
            var awayTeamName = DecodeHtmlText(GetJsonString(gameElement, "VisitingTeamName")) ?? "未知客隊";
            var homeTeamName = DecodeHtmlText(GetJsonString(gameElement, "HomeTeamName")) ?? "未知主隊";

            results.Add(new CpblOfficialGameSnapshot
            {
                GameDate = DateOnly.FromDateTime(localDateTime.DateTime),
                StartTime = TimeOnly.FromDateTime(localDateTime.DateTime),
                AwayTeamCode = MapTeamCode(awayTeamName),
                AwayTeamName = awayTeamName,
                HomeTeamCode = MapTeamCode(homeTeamName),
                HomeTeamName = homeTeamName,
                AwayScore = GetNullableJsonInt(gameElement, "VisitingTotalScore") ?? GetNullableJsonInt(gameElement, "VisitingScore"),
                HomeScore = GetNullableJsonInt(gameElement, "HomeTotalScore") ?? GetNullableJsonInt(gameElement, "HomeScore"),
                Status = BuildGameStatus(gameElement),
                InningText = BuildInningText(gameElement),
                Venue = DecodeHtmlText(GetJsonString(gameElement, "FieldAbbe")),
                VodUrl = DecodeHtmlText(GetJsonString(gameElement, "VodUrl")),
                LiveUrl = DecodeHtmlText(GetJsonString(gameElement, "LiveUrl")),
                WinningPitcherName = DecodeHtmlText(GetJsonString(gameElement, "WinningPitcherName")),
                LosingPitcherName = DecodeHtmlText(GetJsonString(gameElement, "LosePitcherName")),
                VideoCount = GetJsonInt(gameElement, "VideoCount"),
                NewsCount = GetJsonInt(gameElement, "NewsCount"),
                AwayWins = GetNullableJsonInt(gameElement, "VisitingGameResultWCnt"),
                AwayLosses = GetNullableJsonInt(gameElement, "VisitingGameResultLCnt"),
                AwayTies = GetNullableJsonInt(gameElement, "VisitingGameResultTCnt"),
                HomeWins = GetNullableJsonInt(gameElement, "HomeGameResultWCnt"),
                HomeLosses = GetNullableJsonInt(gameElement, "HomeGameResultLCnt"),
                HomeTies = GetNullableJsonInt(gameElement, "HomeGameResultTCnt")
            });
        }

        return results;
    }

    private async Task<IReadOnlyList<CpblOfficialGameSnapshot>> GetCachedGamesAsync(DateOnly targetDate, CancellationToken cancellationToken)
    {
        var cachedResult = await memoryCache.GetOrCreateAsync(
            $"cpbl-official-games:{targetDate:yyyyMMdd}",
            async cacheEntry =>
            {
                // 比賽狀態變動比排名快，這裡的 cache 時間要刻意短一點。
                cacheEntry.AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(2);
                return await GetGamesCoreAsync(targetDate, cancellationToken);
            });

        return cachedResult ?? [];
    }

    private async Task<CpblPlayerStatsResult?> GetPlayerStatsCoreAsync(string accountId, CancellationToken cancellationToken)
    {
        var profile = await GetPlayerProfileAsync(accountId, cancellationToken);
        if (profile is null)
        {
            return null;
        }

        var page = await GetHtmlAsync(BuildPlayerPageUrl(accountId), cancellationToken);
        var battingToken = ExtractAjaxToken(page, "getbattingscore");
        var pitchingToken = ExtractAjaxToken(page, "getpitchscore");

        var latestBatting = default(CpblBattingLine);
        var careerBatting = default(CpblBattingLine);
        if (!string.IsNullOrWhiteSpace(battingToken))
        {
            using var battingResponse = await PostJsonAsync(
                "/team/getbattingscore",
                new Dictionary<string, string>
                {
                    ["acnt"] = accountId,
                    ["kindCode"] = "A"
                },
                battingToken,
                BuildPlayerPageUrl(accountId),
                cancellationToken);

            var battingJson = GetJsonString(battingResponse.RootElement, "BattingScore");
            if (!string.IsNullOrWhiteSpace(battingJson))
            {
                using var battingDocument = JsonDocument.Parse(battingJson);
                var battingLines = battingDocument.RootElement.EnumerateArray()
                    .Select(BuildBattingLine)
                    .Where(line => line.AtBats > 0 || line.PlateAppearances > 0)
                    .OrderByDescending(line => ParseSeason(line.SeasonLabel))
                    .ToList();

                latestBatting = battingLines.FirstOrDefault();
                careerBatting = battingLines.Count == 0 ? null : BuildCareerBattingLine(battingLines);
            }
        }

        var latestPitching = default(CpblPitchingLine);
        var careerPitching = default(CpblPitchingLine);
        if (!string.IsNullOrWhiteSpace(pitchingToken))
        {
            using var pitchingResponse = await PostJsonAsync(
                "/team/getpitchscore",
                new Dictionary<string, string>
                {
                    ["acnt"] = accountId,
                    ["kindCode"] = "A"
                },
                pitchingToken,
                BuildPlayerPageUrl(accountId),
                cancellationToken);

            var pitchingJson = GetJsonString(pitchingResponse.RootElement, "PitchScore");
            if (!string.IsNullOrWhiteSpace(pitchingJson))
            {
                using var pitchingDocument = JsonDocument.Parse(pitchingJson);
                var pitchingLines = pitchingDocument.RootElement.EnumerateArray()
                    .Select(BuildPitchingLine)
                    .Where(line => line.InningsPitched > 0 || line.Games > 0)
                    .OrderByDescending(line => ParseSeason(line.SeasonLabel))
                    .ToList();

                latestPitching = pitchingLines.FirstOrDefault();
                careerPitching = pitchingLines.Count == 0 ? null : BuildCareerPitchingLine(pitchingLines);
            }
        }

        var teamSplits = ShouldLoadBattingTeamSplits(profile, latestBatting, latestPitching)
            ? await GetPlayerTeamSplitsAsync(page, accountId, cancellationToken)
            : [];

        return new CpblPlayerStatsResult
        {
            Profile = profile,
            LatestBatting = latestBatting,
            CareerBatting = careerBatting,
            LatestPitching = latestPitching,
            CareerPitching = careerPitching,
            TeamSplits = teamSplits
        };
    }

    private async Task<IReadOnlyList<CpblTeamStandingSnapshot>> GetStandingsCoreAsync(CancellationToken cancellationToken)
    {
        var homePageHtml = await GetHtmlAsync(GetCandidateBaseUrls(), cancellationToken);

        foreach (Match tableMatch in HomeStandingsTableRegex().Matches(homePageHtml))
        {
            var rows = new List<CpblTeamStandingSnapshot>();
            var tableHtml = tableMatch.Groups["table"].Value;

            foreach (Match rowMatch in HomeStandingRowRegex().Matches(tableHtml))
            {
                var teamName = DecodeHtmlText(rowMatch.Groups["team"].Value);
                if (string.IsNullOrWhiteSpace(teamName))
                {
                    continue;
                }

                rows.Add(new CpblTeamStandingSnapshot
                {
                    Rank = int.TryParse(rowMatch.Groups["rank"].Value, out var rank) ? rank : 0,
                    TeamCode = CpblTeamCatalog.TryResolveTeamCode(teamName, out var teamCode) ? teamCode : teamName,
                    TeamName = teamName,
                    GamesPlayed = int.TryParse(rowMatch.Groups["games"].Value, out var games) ? games : 0,
                    Wins = int.TryParse(rowMatch.Groups["wins"].Value, out var wins) ? wins : 0,
                    Losses = int.TryParse(rowMatch.Groups["losses"].Value, out var losses) ? losses : 0,
                    Ties = int.TryParse(rowMatch.Groups["ties"].Value, out var ties) ? ties : 0,
                    WinningPercentage = decimal.TryParse(rowMatch.Groups["pct"].Value, NumberStyles.Any, CultureInfo.InvariantCulture, out var pct) ? pct : 0m,
                    GamesBehindText = rowMatch.Groups["gb"].Value.Trim(),
                    StreakText = DecodeHtmlText(rowMatch.Groups["streak"].Value) ?? "-"
                });
            }

            if (rows.Count > 0)
            {
                return rows;
            }
        }

        return [];
    }

    private async Task<IReadOnlyList<CpblPlayerTeamSplit>> GetPlayerTeamSplitsAsync(
        string playerPageHtml,
        string accountId,
        CancellationToken cancellationToken)
    {
        var yearToken = ExtractAjaxToken(playerPageHtml, "getfighteryearopts");
        var scoreToken = ExtractAjaxToken(playerPageHtml, "getfighterscore");

        if (string.IsNullOrWhiteSpace(yearToken) || string.IsNullOrWhiteSpace(scoreToken))
        {
            return [];
        }

        using var yearResponse = await PostJsonAsync(
            "/team/getfighteryearopts",
            new Dictionary<string, string>
            {
                ["acnt"] = accountId,
                ["kindCode"] = "A"
            },
            yearToken,
            BuildPlayerPageUrl(accountId),
            cancellationToken);

        var yearJson = GetJsonString(yearResponse.RootElement, "FighterYearOpts");
        if (string.IsNullOrWhiteSpace(yearJson))
        {
            return [];
        }

        using var yearDocument = JsonDocument.Parse(yearJson);
        if (yearDocument.RootElement.ValueKind != JsonValueKind.Array)
        {
            return [];
        }

        var latestYear = yearDocument.RootElement.EnumerateArray()
            .Select(element => GetJsonString(element, "Year"))
            .FirstOrDefault(year => !string.IsNullOrWhiteSpace(year));

        if (string.IsNullOrWhiteSpace(latestYear))
        {
            return [];
        }

        using var scoreResponse = await PostJsonAsync(
            "/team/getfighterscore",
            new Dictionary<string, string>
            {
                ["acnt"] = accountId,
                ["kindCode"] = "A",
                ["year"] = latestYear
            },
            scoreToken,
            BuildPlayerPageUrl(accountId),
            cancellationToken);

        var scoreJson = GetJsonString(scoreResponse.RootElement, "FighterScore");
        if (string.IsNullOrWhiteSpace(scoreJson))
        {
            return [];
        }

        using var scoreDocument = JsonDocument.Parse(scoreJson);
        if (scoreDocument.RootElement.ValueKind != JsonValueKind.Array)
        {
            return [];
        }

        return scoreDocument.RootElement.EnumerateArray()
            .Select(element => BuildPlayerTeamSplit(element, latestYear))
            .Where(split => split.PlateAppearances > 0)
            .OrderByDescending(split => split.Ops)
            .ToList();
    }

    private async Task<CpblPlayerProfile?> GetPlayerProfileAsync(string accountId, CancellationToken cancellationToken)
    {
        return await memoryCache.GetOrCreateAsync(
            $"cpbl-player-profile:{accountId}",
            async cacheEntry =>
            {
                cacheEntry.AbsoluteExpirationRelativeToNow = TimeSpan.FromHours(3);

                var html = await GetHtmlAsync(BuildPlayerPageUrl(accountId), cancellationToken);
                var nameMatch = PlayerNameRegex().Match(html);
                var teamMatch = PlayerTeamRegex().Match(html);
                var name = MatchValue(nameMatch, "name");

                if (string.IsNullOrWhiteSpace(name))
                {
                    return null;
                }

                return new CpblPlayerProfile
                {
                    AccountId = accountId,
                    Name = name,
                    TeamName = MatchValue(teamMatch, "team"),
                    JerseyNumber = MatchValue(nameMatch, "number"),
                    Position = ExtractPlayerDetail(html, "pos"),
                    ThrowsBats = ExtractPlayerDetail(html, "b_t"),
                    HeightWeight = ExtractPlayerDetail(html, "ht_wt"),
                    BirthDate = ExtractPlayerDetail(html, "born")
                };
            });
    }

    private async Task<PlayerDirectoryEntry?> FindPlayerAsync(string playerName, CancellationToken cancellationToken)
    {
        var directory = await GetPlayerDirectoryAsync(cancellationToken);
        var normalizedInput = NormalizeName(playerName);

        return directory.FirstOrDefault(item => string.Equals(item.NormalizedName, normalizedInput, StringComparison.Ordinal)) ??
               directory.FirstOrDefault(item => item.NormalizedName.Contains(normalizedInput, StringComparison.Ordinal)) ??
               directory.FirstOrDefault(item => normalizedInput.Contains(item.NormalizedName, StringComparison.Ordinal));
    }

    private async Task<List<PlayerDirectoryEntry>> GetPlayerDirectoryAsync(CancellationToken cancellationToken)
    {
        return await memoryCache.GetOrCreateAsync(
                   "cpbl-player-directory",
                   async cacheEntry =>
                   {
                       cacheEntry.AbsoluteExpirationRelativeToNow = TimeSpan.FromHours(6);

                       var html = await GetHtmlAsync(GetCandidateBaseUrls().Select(baseUrl => $"{baseUrl}/player"), cancellationToken);
                       var results = new List<PlayerDirectoryEntry>();

                       foreach (Match match in PlayerDirectoryRegex().Matches(html))
                       {
                           var accountId = match.Groups["acnt"].Value.Trim();
                           var name = CleanPlayerName(match.Groups["name"].Value);

                           if (string.IsNullOrWhiteSpace(accountId) || string.IsNullOrWhiteSpace(name))
                           {
                               continue;
                           }

                           results.Add(new PlayerDirectoryEntry(accountId, name, NormalizeName(name)));
                       }

                       return results;
                   }) ??
               [];
    }

    private async Task<string> GetHtmlAsync(string url, CancellationToken cancellationToken)
    {
        return await GetHtmlAsync([url], cancellationToken);
    }

    private async Task<string> GetHtmlAsync(IEnumerable<string> candidateUrls, CancellationToken cancellationToken)
    {
        Exception? lastException = null;

        foreach (var url in candidateUrls.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            try
            {
                using var httpClient = CreateClient();
                using var request = new HttpRequestMessage(HttpMethod.Get, url);
                using var response = await httpClient.SendAsync(request, cancellationToken);
                response.EnsureSuccessStatusCode();
                return await response.Content.ReadAsStringAsync(cancellationToken);
            }
            catch (Exception exception)
            {
                lastException = exception;
                logger.LogDebug(exception, "CPBL GET request failed for {Url}.", url);
            }
        }

        throw new HttpRequestException("Unable to reach any official CPBL host.", lastException);
    }

    private async Task<JsonDocument> PostJsonAsync(
        string relativePath,
        IReadOnlyDictionary<string, string> formValues,
        string? requestVerificationToken,
        string refererUrl,
        CancellationToken cancellationToken)
    {
        return await PostJsonAsync(relativePath, formValues, requestVerificationToken, [refererUrl], cancellationToken);
    }

    private async Task<JsonDocument> PostJsonAsync(
        string relativePath,
        IReadOnlyDictionary<string, string> formValues,
        string? requestVerificationToken,
        IEnumerable<string> refererUrls,
        CancellationToken cancellationToken)
    {
        Exception? lastException = null;

        foreach (var refererUrl in refererUrls.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            try
            {
                using var httpClient = CreateClient();
                using var request = new HttpRequestMessage(HttpMethod.Post, $"{new Uri(refererUrl).GetLeftPart(UriPartial.Authority)}{relativePath}")
                {
                    Content = new FormUrlEncodedContent(formValues)
                };

                request.Headers.Referrer = new Uri(refererUrl);
                request.Headers.Add("X-Requested-With", "XMLHttpRequest");

                if (!string.IsNullOrWhiteSpace(requestVerificationToken))
                {
                    request.Headers.Add("RequestVerificationToken", requestVerificationToken);
                }

                using var response = await httpClient.SendAsync(request, cancellationToken);
                response.EnsureSuccessStatusCode();
                await using var responseStream = await response.Content.ReadAsStreamAsync(cancellationToken);
                return await JsonDocument.ParseAsync(responseStream, cancellationToken: cancellationToken);
            }
            catch (Exception exception)
            {
                lastException = exception;
                logger.LogDebug(exception, "CPBL POST request failed for {RelativePath} via referer {RefererUrl}.", relativePath, refererUrl);
            }
        }

        throw new HttpRequestException($"Unable to post to the official CPBL endpoint {relativePath}.", lastException);
    }

    private HttpClient CreateClient()
    {
        var httpClient = httpClientFactory.CreateClient();
        httpClient.DefaultRequestHeaders.UserAgent.ParseAdd(
            "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/136.0.0.0 Safari/537.36");
        httpClient.Timeout = TimeSpan.FromSeconds(30);
        return httpClient;
    }

    private IEnumerable<string> GetCandidateBaseUrls()
    {
        var configuredBaseUrl = string.IsNullOrWhiteSpace(dataSourceOptions.CpblScheduleBaseUrl)
            ? DefaultOfficialBaseUrl
            : dataSourceOptions.CpblScheduleBaseUrl.TrimEnd('/');

        yield return configuredBaseUrl;

        if (configuredBaseUrl.Contains("://www.", StringComparison.OrdinalIgnoreCase))
        {
            yield return configuredBaseUrl.Replace("://www.", "://", StringComparison.OrdinalIgnoreCase);
        }
        else
        {
            yield return configuredBaseUrl.Replace("://", "://www.", StringComparison.OrdinalIgnoreCase);
        }

        if (!string.Equals(configuredBaseUrl, DefaultOfficialBaseUrl, StringComparison.OrdinalIgnoreCase))
        {
            yield return DefaultOfficialBaseUrl;
        }
    }

    private static string BuildPlayerPageUrl(string accountId) => $"https://www.cpbl.com.tw/team/person?Acnt={accountId}";

    private static string BuildFightingPageUrl(string accountId) => $"https://www.cpbl.com.tw/team/fighting?Acnt={accountId}";

    private static SelectOption? FindTeamOption(IEnumerable<SelectOption> options, string? teamName)
    {
        if (string.IsNullOrWhiteSpace(teamName))
        {
            return options.FirstOrDefault(option => !string.IsNullOrWhiteSpace(option.Value));
        }

        if (CpblTeamCatalog.TryResolveTeamCode(teamName, out var expectedCode))
        {
            var match = options.FirstOrDefault(
                option => CpblTeamCatalog.TryResolveTeamCode(option.Text, out var optionCode) &&
                          string.Equals(optionCode, expectedCode, StringComparison.OrdinalIgnoreCase));

            if (match is not null)
            {
                return match;
            }
        }

        return options.FirstOrDefault(option => option.Text.Contains(teamName, StringComparison.Ordinal));
    }

    private static List<SelectOption> ParseOptionList(string? rawJson)
    {
        if (string.IsNullOrWhiteSpace(rawJson))
        {
            return [];
        }

        using var document = JsonDocument.Parse(rawJson);
        return document.RootElement.EnumerateArray()
            .Select(element => new SelectOption(
                DecodeHtmlText(GetJsonString(element, "Text")) ?? string.Empty,
                GetJsonString(element, "Value") ?? string.Empty))
            .ToList();
    }

    private static CpblBattingLine BuildBattingLine(JsonElement element)
    {
        return new CpblBattingLine
        {
            SeasonLabel = $"{GetJsonString(element, "Year") ?? "未知球季"} 一軍例行賽",
            Games = GetJsonInt(element, "TotalGames"),
            PlateAppearances = GetJsonInt(element, "PlateAppearances"),
            AtBats = GetJsonInt(element, "HitCnt"),
            Hits = GetJsonInt(element, "HittingCnt"),
            RunsBattedIn = GetJsonInt(element, "RunBattedINCnt"),
            Runs = GetJsonInt(element, "ScoreCnt"),
            Doubles = GetJsonInt(element, "TwoBaseHitCnt"),
            Triples = GetJsonInt(element, "ThreeBaseHitCnt"),
            HomeRuns = GetJsonInt(element, "HomeRunCnt"),
            Walks = GetJsonInt(element, "BasesONBallsCnt"),
            HitByPitch = GetJsonInt(element, "HitBYPitchCnt"),
            SacrificeFlies = GetJsonInt(element, "SacrificeFlyCnt"),
            Strikeouts = GetJsonInt(element, "StrikeOutCnt"),
            TotalBases = GetJsonInt(element, "TotalBases"),
            Average = GetJsonDecimal(element, "Avg"),
            OnBasePercentage = GetJsonDecimal(element, "Obp"),
            SluggingPercentage = GetJsonDecimal(element, "Slg"),
            Ops = GetJsonDecimal(element, "Ops"),
            OpsPlus = GetJsonDecimal(element, "OPS_Plus"),
            WrcPlus = GetJsonDecimal(element, "wRC_Plus"),
            Woba = GetJsonDecimal(element, "wOBA"),
            Babip = GetJsonDecimal(element, "BABIP"),
            WalkPercentage = GetJsonDecimal(element, "BB_Pct"),
            StrikeoutPercentage = GetJsonDecimal(element, "K_Pct")
        };
    }

    private static CpblPitchingLine BuildPitchingLine(JsonElement element)
    {
        return new CpblPitchingLine
        {
            SeasonLabel = $"{GetJsonString(element, "Year") ?? "未知球季"} 一軍例行賽",
            Games = GetJsonInt(element, "TotalGames"),
            Starts = GetJsonInt(element, "PitchStarting"),
            Wins = GetJsonInt(element, "Wins"),
            Losses = GetJsonInt(element, "Loses"),
            Saves = GetJsonInt(element, "SaveOK"),
            Strikeouts = GetJsonInt(element, "StrikeOutCnt"),
            Walks = GetJsonInt(element, "BasesONBallsCnt"),
            HitsAllowed = GetJsonInt(element, "HittingCnt"),
            EarnedRuns = GetJsonInt(element, "EarnedRunCnt"),
            InningsPitched = GetJsonDecimal(element, "InningPitched"),
            Era = GetJsonDecimal(element, "Era"),
            Whip = GetJsonDecimal(element, "Whip"),
            KPerNine = GetJsonDecimal(element, "K9"),
            BPerNine = GetJsonDecimal(element, "B9"),
            HPerNine = GetJsonDecimal(element, "H9"),
            Fip = GetJsonDecimal(element, "FIP"),
            EraPlus = GetJsonDecimal(element, "ERA_Plus"),
            WalkPercentage = GetJsonDecimal(element, "BB_Pct"),
            StrikeoutPercentage = GetJsonDecimal(element, "K_Pct")
        };
    }

    private static CpblPlayerTeamSplit BuildPlayerTeamSplit(JsonElement element, string latestYear)
    {
        var teamName = DecodeHtmlText(GetJsonString(element, "FightTeamName")) ?? "未知球隊";
        return new CpblPlayerTeamSplit
        {
            YearLabel = latestYear,
            TeamCode = CpblTeamCatalog.TryResolveTeamCode(teamName, out var teamCode) ? teamCode : teamName,
            TeamName = teamName,
            Games = GetJsonInt(element, "TotalGames"),
            PlateAppearances = GetJsonInt(element, "PlateAppearances"),
            Hits = GetJsonInt(element, "HittingCnt"),
            HomeRuns = GetJsonInt(element, "HomeRunCnt"),
            RunsBattedIn = GetJsonInt(element, "RunBattedINCnt"),
            Average = GetJsonDecimal(element, "Avg"),
            OnBasePercentage = GetJsonDecimal(element, "Obp"),
            SluggingPercentage = GetJsonDecimal(element, "Slg"),
            Ops = GetJsonDecimal(element, "Ops")
        };
    }

    private static CpblBattingLine BuildCareerBattingLine(IReadOnlyList<CpblBattingLine> seasons)
    {
        var atBats = seasons.Sum(line => line.AtBats);
        var hits = seasons.Sum(line => line.Hits);
        var walks = seasons.Sum(line => line.Walks);
        var hitByPitch = seasons.Sum(line => line.HitByPitch);
        var sacrificeFlies = seasons.Sum(line => line.SacrificeFlies);
        var totalBases = seasons.Sum(line => line.TotalBases);

        var average = atBats == 0 ? 0m : Math.Round(hits / (decimal)atBats, 3, MidpointRounding.AwayFromZero);
        var obpDenominator = atBats + walks + hitByPitch + sacrificeFlies;
        var onBasePercentage = obpDenominator == 0
            ? 0m
            : Math.Round((hits + walks + hitByPitch) / (decimal)obpDenominator, 3, MidpointRounding.AwayFromZero);
        var sluggingPercentage = atBats == 0 ? 0m : Math.Round(totalBases / (decimal)atBats, 3, MidpointRounding.AwayFromZero);

        return new CpblBattingLine
        {
            SeasonLabel = "生涯累計",
            Games = seasons.Sum(line => line.Games),
            PlateAppearances = seasons.Sum(line => line.PlateAppearances),
            AtBats = atBats,
            Hits = hits,
            RunsBattedIn = seasons.Sum(line => line.RunsBattedIn),
            Runs = seasons.Sum(line => line.Runs),
            Doubles = seasons.Sum(line => line.Doubles),
            Triples = seasons.Sum(line => line.Triples),
            HomeRuns = seasons.Sum(line => line.HomeRuns),
            Walks = walks,
            HitByPitch = hitByPitch,
            SacrificeFlies = sacrificeFlies,
            Strikeouts = seasons.Sum(line => line.Strikeouts),
            TotalBases = totalBases,
            Average = average,
            OnBasePercentage = onBasePercentage,
            SluggingPercentage = sluggingPercentage,
            Ops = Math.Round(onBasePercentage + sluggingPercentage, 3, MidpointRounding.AwayFromZero)
        };
    }

    private static CpblPitchingLine BuildCareerPitchingLine(IReadOnlyList<CpblPitchingLine> seasons)
    {
        var totalOuts = seasons.Sum(line => BaseballInningsToOuts(line.InningsPitched));
        var hitsAllowed = seasons.Sum(line => line.HitsAllowed);
        var walks = seasons.Sum(line => line.Walks);
        var strikeouts = seasons.Sum(line => line.Strikeouts);
        var earnedRuns = seasons.Sum(line => line.EarnedRuns);

        var era = totalOuts == 0 ? 0m : Math.Round(earnedRuns * 27m / totalOuts, 3, MidpointRounding.AwayFromZero);
        var whip = totalOuts == 0 ? 0m : Math.Round((hitsAllowed + walks) * 3m / totalOuts, 3, MidpointRounding.AwayFromZero);
        var kPerNine = totalOuts == 0 ? 0m : Math.Round(strikeouts * 27m / totalOuts, 3, MidpointRounding.AwayFromZero);
        var bPerNine = totalOuts == 0 ? 0m : Math.Round(walks * 27m / totalOuts, 3, MidpointRounding.AwayFromZero);
        var hPerNine = totalOuts == 0 ? 0m : Math.Round(hitsAllowed * 27m / totalOuts, 3, MidpointRounding.AwayFromZero);

        return new CpblPitchingLine
        {
            SeasonLabel = "生涯累計",
            Games = seasons.Sum(line => line.Games),
            Starts = seasons.Sum(line => line.Starts),
            Wins = seasons.Sum(line => line.Wins),
            Losses = seasons.Sum(line => line.Losses),
            Saves = seasons.Sum(line => line.Saves),
            Strikeouts = strikeouts,
            Walks = walks,
            HitsAllowed = hitsAllowed,
            EarnedRuns = earnedRuns,
            InningsPitched = OutsToBaseballInnings(totalOuts),
            Era = era,
            Whip = whip,
            KPerNine = kPerNine,
            BPerNine = bPerNine,
            HPerNine = hPerNine
        };
    }

    private static DateTimeOffset ParseLocalGameTime(JsonElement gameElement, DateOnly targetDate)
    {
        var preExeDate = ParseNullableDateTime(GetJsonString(gameElement, "PreExeDate"));

        return preExeDate.HasValue
            ? TimeZoneInfo.ConvertTimeBySystemTimeZoneId(preExeDate.Value.UtcDateTime, "Taipei Standard Time")
            : new DateTimeOffset(targetDate.Year, targetDate.Month, targetDate.Day, 18, 35, 0, TimeSpan.FromHours(8));
    }

    private static DateTimeOffset? ParseNullableDateTime(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        if (DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var dateTimeOffset))
        {
            return dateTimeOffset.ToUniversalTime();
        }

        return null;
    }

    private static string BuildGameStatus(JsonElement gameElement)
    {
        var gameStatus = GetNullableJsonInt(gameElement, "GameStatus");
        var presentStatus = GetNullableJsonInt(gameElement, "PresentStatus");

        var isGameStop = GetJsonString(gameElement, "IsGameStop");
        if (gameStatus == 3)
        {
            return "Final";
        }

        if (string.Equals(isGameStop, "1", StringComparison.OrdinalIgnoreCase))
        {
            return "Suspended";
        }

        if (gameStatus == 2 || presentStatus == 1)
        {
            return "Live";
        }

        if (gameStatus == 1)
        {
            return "Scheduled";
        }

        return "Scheduled";
    }

    private static string? BuildInningText(JsonElement gameElement)
    {
        if (gameElement.TryGetProperty("CurtBatting", out var currentBatting) &&
            currentBatting.ValueKind == JsonValueKind.Object &&
            currentBatting.TryGetProperty("InningSeq", out var inningProperty) &&
            inningProperty.TryGetInt32(out var inning))
        {
            return $"第 {inning} 局";
        }

        return null;
    }

    private static string MapTeamCode(string rawTeamName)
    {
        return rawTeamName switch
        {
            "富邦悍將" => "FG",
            "中信兄弟" => "CT",
            "統一7-ELEVEn獅" => "UL",
            "樂天桃猿" => "RA",
            "味全龍" => "WD",
            "台鋼雄鷹" => "TS",
            _ => rawTeamName
        };
    }

    private static string ExtractHomePageToken(string html)
    {
        var match = HomePageTokenRegex().Match(html);
        return match.Success ? match.Groups["token"].Value : string.Empty;
    }

    private static string ExtractAjaxToken(string html, string endpointName)
    {
        var match = Regex.Match(
            html,
            $"url:\\s*\"/team/{Regex.Escape(endpointName)}\".*?RequestVerificationToken:\\s*'(?<token>[^']+)'",
            RegexOptions.IgnoreCase | RegexOptions.Singleline);

        return match.Success ? match.Groups["token"].Value : string.Empty;
    }

    private static string? ExtractPlayerDetail(string html, string cssClass)
    {
        var match = Regex.Match(
            html,
            $"<dd class=\"{Regex.Escape(cssClass)}\">.*?<div class=\"desc\">(?<value>.*?)</div>",
            RegexOptions.IgnoreCase | RegexOptions.Singleline);

        return MatchValue(match, "value");
    }

    private static string? MatchValue(Match match, string groupName)
    {
        if (!match.Success)
        {
            return null;
        }

        return DecodeHtmlText(StripHtml(match.Groups[groupName].Value));
    }

    private static string StripHtml(string rawValue)
    {
        return Regex.Replace(rawValue, "<.*?>", string.Empty).Trim();
    }

    private static string? DecodeHtmlText(string? value)
    {
        return string.IsNullOrWhiteSpace(value) ? null : WebUtility.HtmlDecode(value).Trim();
    }

    private static string CleanPlayerName(string rawValue)
    {
        var decoded = DecodeHtmlText(rawValue) ?? string.Empty;
        return decoded.TrimStart('*', '◎', '．', '.').Trim();
    }

    private static string NormalizeName(string? rawValue)
    {
        var cleaned = CleanPlayerName(rawValue ?? string.Empty);
        return Regex.Replace(cleaned, "\\s+", string.Empty);
    }

    private static int ParseSeason(string seasonLabel)
    {
        var match = Regex.Match(seasonLabel, @"(?<year>\d{4})");
        return match.Success && int.TryParse(match.Groups["year"].Value, out var year) ? year : 0;
    }

    private static string? GetJsonString(JsonElement element, string propertyName)
    {
        if (!element.TryGetProperty(propertyName, out var property))
        {
            return null;
        }

        return property.ValueKind switch
        {
            JsonValueKind.String => property.GetString(),
            JsonValueKind.Number => property.GetRawText(),
            _ => null
        };
    }

    private static int GetJsonInt(JsonElement element, string propertyName)
    {
        return GetNullableJsonInt(element, propertyName) ?? 0;
    }

    private static int? GetNullableJsonInt(JsonElement element, string propertyName)
    {
        if (!element.TryGetProperty(propertyName, out var property))
        {
            return null;
        }

        return property.ValueKind switch
        {
            JsonValueKind.Number when property.TryGetInt32(out var value) => value,
            JsonValueKind.Number => (int)property.GetDouble(),
            JsonValueKind.String when int.TryParse(property.GetString(), NumberStyles.Any, CultureInfo.InvariantCulture, out var value) => value,
            JsonValueKind.String when double.TryParse(property.GetString(), NumberStyles.Any, CultureInfo.InvariantCulture, out var doubleValue) => (int)doubleValue,
            _ => null
        };
    }

    private static decimal GetJsonDecimal(JsonElement element, string propertyName)
    {
        if (!element.TryGetProperty(propertyName, out var property))
        {
            return 0m;
        }

        return property.ValueKind switch
        {
            JsonValueKind.Number => property.GetDecimal(),
            JsonValueKind.String when decimal.TryParse(property.GetString(), NumberStyles.Any, CultureInfo.InvariantCulture, out var value) => value,
            _ => 0m
        };
    }

    private static int BaseballInningsToOuts(decimal inningsPitched)
    {
        var wholeInnings = decimal.Truncate(inningsPitched);
        var decimalPart = (inningsPitched - wholeInnings) * 10m;
        return (int)(wholeInnings * 3m + decimalPart);
    }

    private static decimal OutsToBaseballInnings(int outs)
    {
        var wholeInnings = outs / 3;
        var remainder = outs % 3;
        return wholeInnings + remainder / 10m;
    }

    [GeneratedRegex("name=\"__RequestVerificationToken\"\\s+type=\"hidden\"\\s+value=\"(?<token>[^\"]+)\"", RegexOptions.IgnoreCase)]
    private static partial Regex HomePageTokenRegex();

    private static bool ShouldLoadBattingTeamSplits(CpblPlayerProfile profile, CpblBattingLine? latestBatting, CpblPitchingLine? latestPitching)
    {
        if (profile.Position?.Contains("投手", StringComparison.Ordinal) == true &&
            latestPitching is not null &&
            latestPitching.Games > 0)
        {
            return false;
        }

        return latestBatting is not null && latestBatting.PlateAppearances > 0;
    }

    [GeneratedRegex("<a href=\"/team/person\\?acnt=(?<acnt>\\d+)\"[^>]*>(?<name>.*?)</a>", RegexOptions.IgnoreCase | RegexOptions.Singleline)]
    private static partial Regex PlayerDirectoryRegex();

    [GeneratedRegex("<div class=\"team\">(?<team>.*?)</div>\\s*<div class=\"name\">(?<name>.*?)<span class=\"number\">(?<number>.*?)</span>", RegexOptions.IgnoreCase | RegexOptions.Singleline)]
    private static partial Regex PlayerNameRegex();

    [GeneratedRegex("<dt>\\s*<div class=\"team\">(?<team>.*?)</div>", RegexOptions.IgnoreCase | RegexOptions.Singleline)]
    private static partial Regex PlayerTeamRegex();

    [GeneratedRegex("<div class=\"index_standing_table\">\\s*<table>(?<table>.*?)</table>", RegexOptions.IgnoreCase | RegexOptions.Singleline)]
    private static partial Regex HomeStandingsTableRegex();

    [GeneratedRegex("<tr>\\s*<td class=\"team\">\\s*<div class=\"wrap\">\\s*<div class=\"rank\">(?<rank>\\d+)</div>\\s*<div class=\"team_name\"><a[^>]*title=\"(?<team>[^\"]+)\"[^>]*>.*?</a></div>\\s*</div>\\s*</td>\\s*<td>(?<games>\\d+)</td>\\s*<td>(?<wins>\\d+)-(?<losses>\\d+)-(?<ties>\\d+)</td>\\s*<td>(?<pct>[^<]+)</td>\\s*<td>(?<gb>[^<]+)</td>\\s*<td>(?<streak>[^<]+)</td>\\s*</tr>", RegexOptions.IgnoreCase | RegexOptions.Singleline)]
    private static partial Regex HomeStandingRowRegex();

    private sealed record PlayerDirectoryEntry(string AccountId, string Name, string NormalizedName);

    private sealed record SelectOption(string Text, string Value);
}
