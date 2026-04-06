using CPBLLineBotCloud.Data;
using CPBLLineBotCloud.Models;
using Microsoft.EntityFrameworkCore;

namespace CPBLLineBotCloud.Services;

/// <summary>
/// 整理同步後的賽程資料與官方即時查詢，產出較實用的摘要與推薦內容。
/// </summary>
public class CpblInsightService(
    ApplicationDbContext dbContext,
    ICpblOfficialDataClient officialDataClient,
    ICpblGameSyncService gameSyncService,
    ILogger<CpblInsightService> logger) : ICpblInsightService
{
    private static readonly IReadOnlyDictionary<string, int> RivalryBonusMap = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
    {
        ["CT__UL"] = 8,
        ["UL__CT"] = 8,
        ["CT__RA"] = 7,
        ["RA__CT"] = 7,
        ["FG__CT"] = 6,
        ["CT__FG"] = 6
    };

    public Task<CpblPlayerStatsResult?> GetPlayerStatsAsync(string playerName, CancellationToken cancellationToken = default)
    {
        return officialDataClient.GetPlayerStatsAsync(playerName, cancellationToken);
    }

    public Task<CpblMatchupResult?> GetMatchupAsync(string hitterName, string pitcherName, CancellationToken cancellationToken = default)
    {
        return officialDataClient.GetMatchupAsync(hitterName, pitcherName, cancellationToken);
    }

    public async Task<CpblTeamSummary?> GetTeamSummaryAsync(string teamCode, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(teamCode))
        {
            return null;
        }

        var normalizedTeamCode = teamCode.ToUpperInvariant();
        var today = GetTaipeiToday();

        // 這裡多抓幾天，近期戰績、上一場、下一場都能共用同一批本機資料。
        for (var offset = -8; offset <= 2; offset++)
        {
            try
            {
                await gameSyncService.SyncDateAsync(today.AddDays(offset), cancellationToken);
            }
            catch (Exception exception)
            {
                logger.LogDebug(exception, "Team summary refresh skipped for {TargetDate}.", today.AddDays(offset));
            }
        }

        IReadOnlyList<CpblTeamStandingSnapshot> standings;
        try
        {
            standings = await officialDataClient.GetStandingsAsync(cancellationToken);
        }
        catch (Exception exception)
        {
            logger.LogDebug(exception, "Official standings lookup failed while building team summary for {TeamCode}.", normalizedTeamCode);
            standings = [];
        }

        var recentCompletedGames = await dbContext.Games
            .Where(game =>
                (game.HomeTeamCode == normalizedTeamCode || game.AwayTeamCode == normalizedTeamCode) &&
                game.HomeScore.HasValue &&
                game.AwayScore.HasValue)
            .OrderByDescending(game => game.GameDate)
            .ThenByDescending(game => game.StartTime)
            .Take(8)
            .ToListAsync(cancellationToken);

        var latestTrackedGame = await dbContext.Games
            .Where(game => game.HomeTeamCode == normalizedTeamCode || game.AwayTeamCode == normalizedTeamCode)
            .OrderByDescending(game => game.GameDate)
            .ThenByDescending(game => game.StartTime)
            .FirstOrDefaultAsync(cancellationToken);

        var nextGame = await dbContext.Games
            .Where(game =>
                (game.HomeTeamCode == normalizedTeamCode || game.AwayTeamCode == normalizedTeamCode) &&
                (game.GameDate > today ||
                 (game.GameDate == today && game.Status == "Scheduled")))
            .OrderBy(game => game.GameDate)
            .ThenBy(game => game.StartTime)
            .FirstOrDefaultAsync(cancellationToken);

        var latestNews = await FindLatestTeamNewsAsync(normalizedTeamCode, cancellationToken);

        if (recentCompletedGames.Count == 0 && latestTrackedGame is null && standings.Count == 0)
        {
            return null;
        }

        var recentWins = recentCompletedGames.Count(game => IsTeamWin(game, normalizedTeamCode));
        var recentLosses = recentCompletedGames.Count - recentWins;
        var recentHomeGames = recentCompletedGames.Where(game => game.HomeTeamCode == normalizedTeamCode).ToList();
        var recentAwayGames = recentCompletedGames.Where(game => game.AwayTeamCode == normalizedTeamCode).ToList();

        return new CpblTeamSummary
        {
            TeamCode = normalizedTeamCode,
            TeamName = CpblTeamCatalog.GetDisplayName(normalizedTeamCode),
            Standing = standings.FirstOrDefault(item => string.Equals(item.TeamCode, normalizedTeamCode, StringComparison.OrdinalIgnoreCase)),
            RecentGamesCount = recentCompletedGames.Count,
            RecentWins = recentWins,
            RecentLosses = recentLosses,
            RecentRunsScoredAverage = recentCompletedGames.Count == 0 ? 0m : decimal.Round((decimal)recentCompletedGames.Average(game => GetRunsScored(game, normalizedTeamCode)), 1, MidpointRounding.AwayFromZero),
            RecentRunsAllowedAverage = recentCompletedGames.Count == 0 ? 0m : decimal.Round((decimal)recentCompletedGames.Average(game => GetRunsAllowed(game, normalizedTeamCode)), 1, MidpointRounding.AwayFromZero),
            RecentHomeWins = recentHomeGames.Count(game => IsTeamWin(game, normalizedTeamCode)),
            RecentHomeLosses = recentHomeGames.Count(game => !IsTeamWin(game, normalizedTeamCode)),
            RecentAwayWins = recentAwayGames.Count(game => IsTeamWin(game, normalizedTeamCode)),
            RecentAwayLosses = recentAwayGames.Count(game => !IsTeamWin(game, normalizedTeamCode)),
            RecentTrendText = BuildRecentTrendText(recentCompletedGames, normalizedTeamCode),
            LatestGame = latestTrackedGame,
            NextGame = nextGame,
            LatestNewsTitle = latestNews?.Title
        };
    }

    public async Task<CpblDailyFocus> GetDailyFocusAsync(CancellationToken cancellationToken = default)
    {
        var today = GetTaipeiToday();

        // 每日摘要理論上只看今天附近，但前後都補抓一下，比較能吃到延後更新。
        for (var offset = -1; offset <= 1; offset++)
        {
            try
            {
                await gameSyncService.SyncDateAsync(today.AddDays(offset), cancellationToken);
            }
            catch (Exception exception)
            {
                logger.LogDebug(exception, "Daily focus refresh skipped for {TargetDate}.", today.AddDays(offset));
            }
        }

        var todayGames = await dbContext.Games
            .Where(game => game.GameDate == today)
            .OrderBy(game => game.StartTime)
            .ToListAsync(cancellationToken);

        var tomorrowGames = await dbContext.Games
            .Where(game => game.GameDate == today.AddDays(1))
            .OrderBy(game => game.StartTime)
            .ToListAsync(cancellationToken);

        var latestNews = await dbContext.NewsItems
            .OrderByDescending(news => news.PublishTime)
            .FirstOrDefaultAsync(cancellationToken);

        var items = new List<string>();
        var recommendation = await GetTodayBestGameAsync(cancellationToken);
        if (recommendation is not null)
        {
            items.Add($"今日推薦：{recommendation.GameLabel}");
        }

        if (todayGames.Count > 1)
        {
            var secondaryGame = todayGames
                .OrderByDescending(game => game.StartTime)
                .FirstOrDefault(game => recommendation is null || !recommendation.GameLabel.Contains(CpblTeamCatalog.GetDisplayName(game.HomeTeamCode), StringComparison.Ordinal));

            if (secondaryGame is not null)
            {
                items.Add($"其他可留意：{BuildCompactGameLine(secondaryGame)}");
            }
        }

        if (latestNews is not null)
        {
            items.Add($"新聞焦點：{latestNews.Title}");
        }

        var nextGame = tomorrowGames.FirstOrDefault() ??
                       todayGames.FirstOrDefault(game => string.Equals(game.Status, "Scheduled", StringComparison.OrdinalIgnoreCase));

        if (nextGame is not null)
        {
            items.Add($"接下來可看：{BuildUpcomingGameLine(nextGame)}");
        }

        if (items.Count == 0)
        {
            items.Add("今天暫時沒有可整理的賽事或新聞資料，稍晚再試一次");
        }

        return new CpblDailyFocus
        {
            FocusDate = today,
            Items = items.Take(4).ToList()
        };
    }

    public async Task<CpblGameRecommendation?> GetTodayBestGameAsync(CancellationToken cancellationToken = default)
    {
        var today = GetTaipeiToday();

        // 今日推薦會參考近況，所以先把前幾天的資料補齊再算。
        for (var offset = -5; offset <= 0; offset++)
        {
            try
            {
                await gameSyncService.SyncDateAsync(today.AddDays(offset), cancellationToken);
            }
            catch (Exception exception)
            {
                logger.LogDebug(exception, "Recommendation refresh skipped for {TargetDate}.", today.AddDays(offset));
            }
        }

        var todayGames = await dbContext.Games
            .Where(game => game.GameDate == today)
            .OrderBy(game => game.StartTime)
            .ToListAsync(cancellationToken);

        if (todayGames.Count == 0)
        {
            return null;
        }

        IReadOnlyList<CpblTeamStandingSnapshot> standings;
        try
        {
            standings = await officialDataClient.GetStandingsAsync(cancellationToken);
        }
        catch (Exception exception)
        {
            logger.LogDebug(exception, "Official standings lookup failed while building today-best recommendation.");
            standings = [];
        }

        var scoredGames = new List<(GameInfo Game, decimal Score, IReadOnlyList<string> Reasons)>();

        foreach (var game in todayGames)
        {
            var awayGames = await GetRecentCompletedGamesAsync(game.AwayTeamCode, cancellationToken);
            var homeGames = await GetRecentCompletedGamesAsync(game.HomeTeamCode, cancellationToken);

            var awayWinRate = GetWinRate(awayGames, game.AwayTeamCode);
            var homeWinRate = GetWinRate(homeGames, game.HomeTeamCode);
            var combinedFormScore = decimal.Round(((awayWinRate + homeWinRate) / 2m) * 25m, 1, MidpointRounding.AwayFromZero);
            var competitionScore = decimal.Round((1m - Math.Abs(awayWinRate - homeWinRate)) * 20m, 1, MidpointRounding.AwayFromZero);

            var standingsScore = 0m;
            var standingsReason = string.Empty;
            var awayStanding = standings.FirstOrDefault(item => string.Equals(item.TeamCode, game.AwayTeamCode, StringComparison.OrdinalIgnoreCase));
            var homeStanding = standings.FirstOrDefault(item => string.Equals(item.TeamCode, game.HomeTeamCode, StringComparison.OrdinalIgnoreCase));

            if (awayStanding is not null && homeStanding is not null)
            {
                var rankGap = Math.Abs(awayStanding.Rank - homeStanding.Rank);
                standingsScore = rankGap switch
                {
                    0 => 25m,
                    1 => 22m,
                    2 => 16m,
                    _ => 8m
                };

                standingsReason = rankGap <= 1
                    ? "兩隊目前排名咬得很近，這場對戰的體感張力比較高"
                    : "這場會影響近期排名節奏，還是有觀察價值";
            }

            var rivalryKey = $"{game.AwayTeamCode}__{game.HomeTeamCode}";
            var rivalryBonus = RivalryBonusMap.TryGetValue(rivalryKey, out var mappedBonus) ? mappedBonus : 0;
            var score = combinedFormScore + competitionScore + standingsScore + rivalryBonus;

            var reasons = new List<string>();
            reasons.Add(combinedFormScore >= 17m
                ? "兩隊最近都不是冷手，近期戰績有支撐"
                : "至少有一隊近期狀態不錯，值得觀察");

            reasons.Add(competitionScore >= 14m
                ? "近況接近，這場比較像有機會一路咬到後段"
                : "兩隊近況差距稍大，但比賽內容仍有話題");

            if (!string.IsNullOrWhiteSpace(standingsReason))
            {
                reasons.Add(standingsReason);
            }

            if (rivalryBonus > 0)
            {
                reasons.Add("這組對戰本來就比較有話題性，拿來追很合理");
            }

            scoredGames.Add((game, score, reasons.Take(3).ToList()));
        }

        var bestGame = scoredGames
            .OrderByDescending(item => item.Score)
            .ThenBy(item => item.Game.StartTime)
            .First();

        return new CpblGameRecommendation
        {
            FocusDate = today,
            GameLabel = $"{CpblTeamCatalog.GetDisplayName(bestGame.Game.AwayTeamCode)} vs {CpblTeamCatalog.GetDisplayName(bestGame.Game.HomeTeamCode)}",
            StartTimeText = bestGame.Game.StartTime?.ToString("HH:mm") ?? "--:--",
            VenueText = string.IsNullOrWhiteSpace(bestGame.Game.Venue) ? "待公告" : bestGame.Game.Venue,
            Reasons = bestGame.Reasons
        };
    }

    public async Task<IReadOnlyList<CpblOfficialGameSnapshot>> GetRecentHighlightsAsync(int lookbackDays, CancellationToken cancellationToken = default)
    {
        var today = GetTaipeiToday();
        var results = new List<CpblOfficialGameSnapshot>();

        for (var dayOffset = 0; dayOffset <= Math.Max(1, lookbackDays); dayOffset++)
        {
            var targetDate = today.AddDays(-dayOffset);
            var games = await officialDataClient.GetGamesAsync(targetDate, cancellationToken);

            results.AddRange(games.Where(game =>
                string.Equals(game.Status, "Final", StringComparison.OrdinalIgnoreCase) &&
                (!string.IsNullOrWhiteSpace(game.VodUrl) || game.VideoCount > 0)));
        }

        return results
            .OrderByDescending(game => game.GameDate)
            .ThenByDescending(game => game.StartTime)
            .Take(4)
            .ToList();
    }

    public async Task<IReadOnlyList<CpblScorePrediction>> GetPredictionsAsync(
        DateOnly targetDate,
        string? awayTeamCode = null,
        string? homeTeamCode = null,
        CancellationToken cancellationToken = default)
    {
        var refreshStart = targetDate.AddDays(-6);
        for (var date = refreshStart; date <= targetDate; date = date.AddDays(1))
        {
            try
            {
                await gameSyncService.SyncDateAsync(date, cancellationToken);
            }
            catch (Exception exception)
            {
                logger.LogDebug(exception, "Prediction refresh skipped for {TargetDate}.", date);
            }
        }

        if (!string.IsNullOrWhiteSpace(awayTeamCode) && !string.IsNullOrWhiteSpace(homeTeamCode))
        {
            var singlePrediction = await BuildPredictionAsync(awayTeamCode, homeTeamCode, cancellationToken);
            return singlePrediction is null ? [] : [singlePrediction];
        }

        var games = await dbContext.Games
            .Where(game => game.GameDate == targetDate)
            .OrderBy(game => game.StartTime)
            .ToListAsync(cancellationToken);

        var predictions = new List<CpblScorePrediction>();
        foreach (var game in games)
        {
            var prediction = await BuildPredictionAsync(game.AwayTeamCode, game.HomeTeamCode, cancellationToken);
            if (prediction is not null)
            {
                predictions.Add(prediction);
            }
        }

        return predictions;
    }

    private async Task<CpblScorePrediction?> BuildPredictionAsync(string awayTeamCode, string homeTeamCode, CancellationToken cancellationToken)
    {
        var awayGames = await GetRecentCompletedGamesAsync(awayTeamCode, cancellationToken);
        var homeGames = await GetRecentCompletedGamesAsync(homeTeamCode, cancellationToken);

        if (awayGames.Count == 0 || homeGames.Count == 0)
        {
            return null;
        }

        var awayOffense = awayGames.Average(game => GetRunsScored(game, awayTeamCode));
        var awayDefense = awayGames.Average(game => GetRunsAllowed(game, awayTeamCode));
        var awayWinRate = awayGames.Count(game => IsTeamWin(game, awayTeamCode)) / (decimal)awayGames.Count;

        var homeOffense = homeGames.Average(game => GetRunsScored(game, homeTeamCode));
        var homeDefense = homeGames.Average(game => GetRunsAllowed(game, homeTeamCode));
        var homeWinRate = homeGames.Count(game => IsTeamWin(game, homeTeamCode)) / (decimal)homeGames.Count;

        var awayExpected = decimal.Round((decimal)((awayOffense + homeDefense) / 2d), 1, MidpointRounding.AwayFromZero);
        var homeExpected = decimal.Round((decimal)((homeOffense + awayDefense) / 2d) + 0.4m, 1, MidpointRounding.AwayFromZero);

        var strengthDelta = homeWinRate - awayWinRate;
        if (strengthDelta > 0)
        {
            homeExpected += decimal.Round(strengthDelta, 1, MidpointRounding.AwayFromZero);
        }
        else if (strengthDelta < 0)
        {
            awayExpected += decimal.Round(Math.Abs(strengthDelta), 1, MidpointRounding.AwayFromZero);
        }

        awayExpected = decimal.Clamp(awayExpected, 1.2m, 9.5m);
        homeExpected = decimal.Clamp(homeExpected, 1.2m, 9.5m);

        var runGap = Math.Abs(homeExpected - awayExpected);
        var confidence = runGap >= 1.8m ? "偏高" : runGap >= 1.0m ? "中等" : "接近五五波";
        var insight =
            $"{CpblTeamCatalog.GetDisplayName(awayTeamCode)}近 {awayGames.Count} 戰場均得 {awayOffense:F1}、失 {awayDefense:F1}；" +
            $"{CpblTeamCatalog.GetDisplayName(homeTeamCode)}近 {homeGames.Count} 戰場均得 {homeOffense:F1}、失 {homeDefense:F1}";

        return new CpblScorePrediction
        {
            AwayTeamCode = awayTeamCode,
            AwayTeamName = CpblTeamCatalog.GetDisplayName(awayTeamCode),
            HomeTeamCode = homeTeamCode,
            HomeTeamName = CpblTeamCatalog.GetDisplayName(homeTeamCode),
            AwayExpectedRuns = awayExpected,
            HomeExpectedRuns = homeExpected,
            ConfidenceLabel = confidence,
            Insight = insight
        };
    }

    private Task<List<GameInfo>> GetRecentCompletedGamesAsync(string teamCode, CancellationToken cancellationToken)
    {
        return dbContext.Games
            .Where(game =>
                (game.HomeTeamCode == teamCode || game.AwayTeamCode == teamCode) &&
                game.HomeScore.HasValue &&
                game.AwayScore.HasValue)
            .OrderByDescending(game => game.GameDate)
            .ThenByDescending(game => game.StartTime)
            .Take(8)
            .ToListAsync(cancellationToken);
    }

    private async Task<NewsInfo?> FindLatestTeamNewsAsync(string teamCode, CancellationToken cancellationToken)
    {
        var keywords = CpblTeamCatalog.GetSearchKeywords(teamCode);
        var recentNews = await dbContext.NewsItems
            .OrderByDescending(news => news.PublishTime)
            .Take(50)
            .ToListAsync(cancellationToken);

        return recentNews.FirstOrDefault(news =>
            keywords.Any(keyword =>
                news.Title.Contains(keyword, StringComparison.OrdinalIgnoreCase) ||
                (!string.IsNullOrWhiteSpace(news.Summary) && news.Summary.Contains(keyword, StringComparison.OrdinalIgnoreCase))));
    }

    private static decimal GetWinRate(IReadOnlyList<GameInfo> games, string teamCode)
    {
        if (games.Count == 0)
        {
            return 0.5m;
        }

        return games.Count(game => IsTeamWin(game, teamCode)) / (decimal)games.Count;
    }

    private static string BuildRecentTrendText(IReadOnlyList<GameInfo> games, string teamCode)
    {
        if (games.Count == 0)
        {
            return "近期走勢暫時沒有樣本";
        }

        var trendMarks = games
            .OrderBy(game => game.GameDate)
            .ThenBy(game => game.StartTime)
            .Select(game => IsTeamWin(game, teamCode) ? "勝" : "敗");

        return string.Join(" ", trendMarks);
    }

    private static string BuildCompactGameLine(GameInfo game)
    {
        var awayName = CpblTeamCatalog.GetDisplayName(game.AwayTeamCode);
        var homeName = CpblTeamCatalog.GetDisplayName(game.HomeTeamCode);
        var venue = string.IsNullOrWhiteSpace(game.Venue) ? "場地待公告" : game.Venue;
        var status = BuildLocalizedStatus(game);

        if (game.AwayScore.HasValue && game.HomeScore.HasValue)
        {
            return $"{awayName} {game.AwayScore}:{game.HomeScore} {homeName}，{status}，地點 {venue}";
        }

        var startTime = game.StartTime?.ToString("HH:mm") ?? "--:--";
        return $"{awayName} 對 {homeName}，{startTime} 開打，{status}，地點 {venue}";
    }

    private static string BuildUpcomingGameLine(GameInfo game)
    {
        var awayName = CpblTeamCatalog.GetDisplayName(game.AwayTeamCode);
        var homeName = CpblTeamCatalog.GetDisplayName(game.HomeTeamCode);
        var startDateTime = game.StartTime.HasValue
            ? $"{game.GameDate:MM/dd} {game.StartTime:HH:mm}"
            : $"{game.GameDate:MM/dd} 待公告";

        return $"{awayName} vs {homeName}，{startDateTime}，{game.Venue ?? "場地待公告"}";
    }

    private static int GetRunsScored(GameInfo game, string teamCode)
    {
        return game.HomeTeamCode == teamCode ? game.HomeScore ?? 0 : game.AwayScore ?? 0;
    }

    private static int GetRunsAllowed(GameInfo game, string teamCode)
    {
        return game.HomeTeamCode == teamCode ? game.AwayScore ?? 0 : game.HomeScore ?? 0;
    }

    private static bool IsTeamWin(GameInfo game, string teamCode)
    {
        return GetRunsScored(game, teamCode) > GetRunsAllowed(game, teamCode);
    }

    private static string BuildLocalizedStatus(GameInfo game)
    {
        var taipeiNow = GetTaipeiNow();
        var taipeiToday = DateOnly.FromDateTime(taipeiNow);
        var taipeiCurrentTime = TimeOnly.FromDateTime(taipeiNow);

        if (game.GameDate > taipeiToday)
        {
            return "尚未開打";
        }

        if (game.GameDate == taipeiToday &&
            string.Equals(game.Status, "Live", StringComparison.OrdinalIgnoreCase) &&
            string.IsNullOrWhiteSpace(game.InningText) &&
            game.StartTime.HasValue &&
            game.StartTime.Value > taipeiCurrentTime.AddMinutes(5))
        {
            return "尚未開打";
        }

        return game.Status switch
        {
            "Live" when !string.IsNullOrWhiteSpace(game.InningText) => $"進行中，{game.InningText}",
            "Live" => "進行中",
            "Final" => "終場",
            "Suspended" => "暫停或延賽",
            _ => "尚未開打"
        };
    }

    private static DateOnly GetTaipeiToday()
    {
        return DateOnly.FromDateTime(GetTaipeiNow());
    }

    private static DateTime GetTaipeiNow()
    {
        return TimeZoneInfo.ConvertTimeBySystemTimeZoneId(DateTime.UtcNow, "Taipei Standard Time");
    }
}
