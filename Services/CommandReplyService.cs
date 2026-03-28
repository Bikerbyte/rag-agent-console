using System.Text;
using System.Text.RegularExpressions;
using CPBLLineBotCloud.Data;
using CPBLLineBotCloud.Models;
using Microsoft.EntityFrameworkCore;

namespace CPBLLineBotCloud.Services;

public class CommandReplyService(
    ApplicationDbContext dbContext,
    ICpblGameSyncService cpblGameSyncService,
    IBaseballNewsSyncService baseballNewsSyncService,
    ICpblInsightService cpblInsightService,
    ILogger<CommandReplyService> logger) : ICommandReplyService
{
    public async Task<string> BuildReplyAsync(string commandText, string? chatId = null, CancellationToken cancellationToken = default)
    {
        var normalizedCommand = NormalizeCommand(commandText);
        if (string.IsNullOrWhiteSpace(normalizedCommand))
        {
            return BuildHelpReply();
        }

        logger.LogInformation("Building command reply for text: {CommandText}", normalizedCommand);

        if (IsFollowCommand(normalizedCommand, out var teamInput))
        {
            return await BuildFollowReplyAsync(chatId, teamInput, cancellationToken);
        }

        if (IsUnfollowCommand(normalizedCommand))
        {
            return await BuildUnfollowReplyAsync(chatId, cancellationToken);
        }

        if (IsMyFollowCommand(normalizedCommand))
        {
            return await BuildMyFollowReplyAsync(chatId, cancellationToken);
        }

        if (IsTodayBestCommand(normalizedCommand))
        {
            return await BuildTodayBestReplyAsync(cancellationToken);
        }

        if (IsRecapCommand(normalizedCommand))
        {
            return await BuildRecapReplyAsync(chatId, cancellationToken);
        }

        if (IsTodayScheduleCommand(normalizedCommand))
        {
            return await BuildScheduleReplyAsync(0, "今日賽程", cancellationToken);
        }

        if (IsTomorrowScheduleCommand(normalizedCommand))
        {
            return await BuildScheduleReplyAsync(1, "明日賽程", cancellationToken);
        }

        if (IsLatestNewsCommand(normalizedCommand))
        {
            return await BuildLatestNewsReplyAsync(cancellationToken);
        }

        if (TryExtractTeamCommand(normalizedCommand, out var teamCode, out var commandKind))
        {
            return commandKind switch
            {
                TeamCommandKind.TeamToday => await BuildTeamScheduleReplyAsync(teamCode, 0, cancellationToken),
                TeamCommandKind.TeamTomorrow => await BuildTeamScheduleReplyAsync(teamCode, 1, cancellationToken),
                _ => await BuildTeamSummaryReplyAsync(teamCode, cancellationToken)
            };
        }

        if (IsHelpCommand(normalizedCommand))
        {
            return BuildHelpReply();
        }

        return BuildHelpReply();
    }

    private async Task<string> BuildScheduleReplyAsync(int dayOffset, string heading, CancellationToken cancellationToken)
    {
        var targetDate = GetTaipeiToday().AddDays(dayOffset);
        await TryRefreshGameDateAsync(targetDate, cancellationToken);

        var games = await dbContext.Games
            .Where(game => game.GameDate == targetDate)
            .OrderBy(game => game.StartTime)
            .ToListAsync(cancellationToken);

        if (games.Count == 0)
        {
            return $"{heading}\n{targetDate:yyyy/MM/dd}\n目前沒有賽程，或官方來源尚未提供資料。";
        }

        var replyBuilder = new StringBuilder();
        replyBuilder.AppendLine($"{heading} | {targetDate:yyyy/MM/dd}");

        for (var index = 0; index < games.Count; index++)
        {
            var game = games[index];
            var awayName = CpblTeamCatalog.GetDisplayName(game.AwayTeamCode);
            var homeName = CpblTeamCatalog.GetDisplayName(game.HomeTeamCode);
            var timeText = game.StartTime?.ToString("HH:mm") ?? "--:--";

            replyBuilder.AppendLine($"{index + 1}. {awayName} vs {homeName}");
            replyBuilder.AppendLine($"   開賽: {timeText} | 地點: {game.Venue ?? "待公告"}");

            var localizedStatus = BuildLocalizedStatus(game);
            replyBuilder.AppendLine($"   狀態: {localizedStatus}");

            if (ShouldDisplayScore(game, localizedStatus))
            {
                replyBuilder.AppendLine($"   比分: {awayName} {game.AwayScore} : {game.HomeScore} {homeName}");
            }
        }

        replyBuilder.AppendLine("資料來源: CPBL 官方網站");
        return replyBuilder.ToString().TrimEnd();
    }

    private async Task<string> BuildTodayBestReplyAsync(CancellationToken cancellationToken)
    {
        var recommendation = await cpblInsightService.GetTodayBestGameAsync(cancellationToken);
        if (recommendation is null)
        {
            return "今日最值得看\n今天目前沒有可推薦的比賽，或資料還在同步中。";
        }

        var replyBuilder = new StringBuilder();
        replyBuilder.AppendLine($"今日最值得看 | {recommendation.FocusDate:yyyy/MM/dd}");
        replyBuilder.AppendLine(recommendation.GameLabel);
        replyBuilder.AppendLine($"開賽: {recommendation.StartTimeText} | 地點: {recommendation.VenueText}");
        replyBuilder.AppendLine("推薦理由:");

        foreach (var reason in recommendation.Reasons)
        {
            replyBuilder.AppendLine($"- {reason}");
        }

        replyBuilder.AppendLine("如果你還想繼續查，可以直接輸入：今天有比賽嗎 / 有什麼最新新聞 / /follow 兄弟");
        return replyBuilder.ToString().TrimEnd();
    }

    private async Task<string> BuildTeamSummaryReplyAsync(string teamCode, CancellationToken cancellationToken)
    {
        await TryRefreshNewsAsync(cancellationToken);

        CpblTeamSummary? summary;
        try
        {
            summary = await cpblInsightService.GetTeamSummaryAsync(teamCode, cancellationToken);
        }
        catch (Exception exception)
        {
            logger.LogWarning(exception, "Team summary lookup failed for {TeamCode}. Falling back to local synced data.", teamCode);
            return await BuildTeamSummaryFallbackReplyAsync(teamCode, cancellationToken);
        }

        if (summary is null)
        {
            return await BuildTeamSummaryFallbackReplyAsync(teamCode, cancellationToken);
        }

        var replyBuilder = new StringBuilder();
        replyBuilder.AppendLine($"{summary.TeamName}近況");

        if (summary.Standing is not null)
        {
            replyBuilder.AppendLine(
                $"官方排名: 第 {summary.Standing.Rank} 名 | 戰績 {summary.Standing.Wins}-{summary.Standing.Losses}-{summary.Standing.Ties} | 勝率 {summary.Standing.WinningPercentage:0.000} | 勝差 {summary.Standing.GamesBehindText} | 連況 {summary.Standing.StreakText}");
        }

        if (summary.RecentGamesCount > 0)
        {
            var runDiff = Math.Round(summary.RecentRunsScoredAverage - summary.RecentRunsAllowedAverage, 1, MidpointRounding.AwayFromZero);
            replyBuilder.AppendLine($"近 {summary.RecentGamesCount} 場: {summary.RecentWins} 勝 {summary.RecentLosses} 敗 | 場均攻守差 {runDiff:+0.0;-0.0;0.0}");
            replyBuilder.AppendLine($"狀態判讀: {BuildTeamTemperatureText(summary)}");
            replyBuilder.AppendLine($"近期走勢: {summary.RecentTrendText}");
        }

        if (summary.LatestGame is not null)
        {
            replyBuilder.AppendLine($"最近一場: {BuildGameSummary(summary.LatestGame)}");
        }

        if (summary.NextGame is not null &&
            (summary.LatestGame is null || summary.NextGame.GameDate >= summary.LatestGame.GameDate))
        {
            replyBuilder.AppendLine($"下一場: {BuildGameSummary(summary.NextGame)}");
        }

        if (!string.IsNullOrWhiteSpace(summary.LatestNewsTitle))
        {
            replyBuilder.AppendLine($"相關新聞: {summary.LatestNewsTitle}");
        }

        replyBuilder.AppendLine($"你也可以接著輸入：{summary.TeamName}今天有沒有比賽 / /follow {summary.TeamName} / 今日最值得看");
        return replyBuilder.ToString().TrimEnd();
    }

    private async Task<string> BuildTeamSummaryFallbackReplyAsync(string teamCode, CancellationToken cancellationToken)
    {
        var normalizedTeamCode = teamCode.ToUpperInvariant();
        var today = GetTaipeiToday();
        var teamName = CpblTeamCatalog.GetDisplayName(normalizedTeamCode);

        var recentCompletedGames = await dbContext.Games
            .Where(game =>
                (game.HomeTeamCode == normalizedTeamCode || game.AwayTeamCode == normalizedTeamCode) &&
                game.HomeScore.HasValue &&
                game.AwayScore.HasValue)
            .OrderByDescending(game => game.GameDate)
            .ThenByDescending(game => game.StartTime)
            .Take(5)
            .ToListAsync(cancellationToken);

        var nextGame = await dbContext.Games
            .Where(game =>
                (game.HomeTeamCode == normalizedTeamCode || game.AwayTeamCode == normalizedTeamCode) &&
                (game.GameDate > today ||
                 (game.GameDate == today && game.Status == "Scheduled")))
            .OrderBy(game => game.GameDate)
            .ThenBy(game => game.StartTime)
            .FirstOrDefaultAsync(cancellationToken);

        if (recentCompletedGames.Count == 0 && nextGame is null)
        {
            return $"{teamName}\n官方即時資料暫時有點不穩，我這邊也還沒有足夠的已同步資料。";
        }

        var wins = recentCompletedGames.Count(game => IsTeamWin(game, normalizedTeamCode));
        var losses = recentCompletedGames.Count - wins;

        var replyBuilder = new StringBuilder();
        replyBuilder.AppendLine($"{teamName}近況");
        replyBuilder.AppendLine("官方排名暫時抓不到，先給你目前已同步的資料。");

        if (recentCompletedGames.Count > 0)
        {
            replyBuilder.AppendLine($"近 {recentCompletedGames.Count} 場: {wins} 勝 {losses} 敗");
            replyBuilder.AppendLine($"最近一場: {BuildGameSummary(recentCompletedGames[0])}");
        }

        if (nextGame is not null)
        {
            replyBuilder.AppendLine($"下一場: {BuildGameSummary(nextGame)}");
        }

        replyBuilder.AppendLine("你也可以直接輸入：有什麼最新新聞 / 今日最值得看 / 今天有什麼比賽");
        return replyBuilder.ToString().TrimEnd();
    }

    private async Task<string> BuildTeamScheduleReplyAsync(string teamCode, int dayOffset, CancellationToken cancellationToken)
    {
        var targetDate = GetTaipeiToday().AddDays(dayOffset);
        await TryRefreshGameDateAsync(targetDate, cancellationToken);

        var games = await dbContext.Games
            .Where(game =>
                game.GameDate == targetDate &&
                (game.HomeTeamCode == teamCode || game.AwayTeamCode == teamCode))
            .OrderBy(game => game.StartTime)
            .ToListAsync(cancellationToken);

        var teamName = CpblTeamCatalog.GetDisplayName(teamCode);
        var dayLabel = dayOffset == 0 ? "今天" : "明天";

        if (games.Count == 0)
        {
            return $"{teamName}\n{targetDate:yyyy/MM/dd} {dayLabel}目前沒有排到比賽。";
        }

        var replyBuilder = new StringBuilder();
        replyBuilder.AppendLine($"{teamName} | {targetDate:yyyy/MM/dd}");

        for (var index = 0; index < games.Count; index++)
        {
            var game = games[index];
            var opponentCode = string.Equals(game.HomeTeamCode, teamCode, StringComparison.OrdinalIgnoreCase)
                ? game.AwayTeamCode
                : game.HomeTeamCode;
            var homeAwayLabel = string.Equals(game.HomeTeamCode, teamCode, StringComparison.OrdinalIgnoreCase) ? "主場" : "客場";
            var startTime = game.StartTime?.ToString("HH:mm") ?? "--:--";

            replyBuilder.AppendLine($"{index + 1}. {homeAwayLabel}對 {CpblTeamCatalog.GetDisplayName(opponentCode)}");
            replyBuilder.AppendLine($"   開賽: {startTime} | 地點: {game.Venue ?? "待公告"}");

            var localizedStatus = BuildLocalizedStatus(game);
            replyBuilder.AppendLine($"   狀態: {localizedStatus}");

            if (ShouldDisplayScore(game, localizedStatus))
            {
                var awayName = CpblTeamCatalog.GetDisplayName(game.AwayTeamCode);
                var homeName = CpblTeamCatalog.GetDisplayName(game.HomeTeamCode);
                replyBuilder.AppendLine($"   比分: {awayName} {game.AwayScore} : {game.HomeScore} {homeName}");
            }
        }

        return replyBuilder.ToString().TrimEnd();
    }

    private async Task<string> BuildLatestNewsReplyAsync(CancellationToken cancellationToken)
    {
        await TryRefreshNewsAsync(cancellationToken);

        var latestNews = await dbContext.NewsItems
            .OrderByDescending(news => news.PublishTime)
            .Take(3)
            .ToListAsync(cancellationToken);

        if (latestNews.Count == 0)
        {
            return "最新新聞\n目前沒有已同步的新聞資料。";
        }

        var replyBuilder = new StringBuilder();
        replyBuilder.AppendLine("最新新聞");

        for (var index = 0; index < latestNews.Count; index++)
        {
            var news = latestNews[index];
            replyBuilder.AppendLine($"{index + 1}. {news.Title}");
            replyBuilder.AppendLine($"   日期: {news.PublishTime.ToOffset(TimeSpan.FromHours(8)):yyyy/MM/dd HH:mm} | 來源: {news.SourceName}");

            if (!string.IsNullOrWhiteSpace(news.Summary))
            {
                replyBuilder.AppendLine($"   摘要: {news.Summary}");
            }
        }

        return replyBuilder.ToString().TrimEnd();
    }

    private async Task<string> BuildRecapReplyAsync(string? chatId, CancellationToken cancellationToken)
    {
        var today = GetTaipeiToday();
        await TryRefreshGameDateAsync(today, cancellationToken);

        var finalGames = await dbContext.Games
            .Where(game => game.GameDate == today && game.Status == "Final" && game.HomeScore.HasValue && game.AwayScore.HasValue)
            .OrderBy(game => game.StartTime)
            .ToListAsync(cancellationToken);

        if (finalGames.Count == 0)
        {
            return "今日 recap\n今天目前還沒有已完賽結果，稍晚再來看一次。";
        }

        var followedTeamCode = await GetFollowedTeamCodeAsync(chatId, cancellationToken);
        var replyBuilder = new StringBuilder();
        replyBuilder.AppendLine($"今日 recap | {today:yyyy/MM/dd}");

        if (!string.IsNullOrWhiteSpace(followedTeamCode))
        {
            var followedGame = finalGames.FirstOrDefault(game => IsTrackedTeamGame(game, followedTeamCode));
            if (followedGame is not null)
            {
                replyBuilder.AppendLine($"你追蹤的球隊: {BuildTrackedTeamRecapLine(followedGame, followedTeamCode)}");
                replyBuilder.AppendLine(string.Empty);
            }
        }

        foreach (var game in finalGames)
        {
            replyBuilder.AppendLine($"- {BuildNeutralRecapLine(game)}");
        }

        return replyBuilder.ToString().TrimEnd();
    }

    private async Task<string> BuildFollowReplyAsync(string? chatId, string teamInput, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(chatId))
        {
            return "追蹤球隊\n這個功能需要直接在 Telegram 聊天視窗裡使用。";
        }

        if (!CpblTeamCatalog.TryResolveTeamCode(teamInput, out var teamCode))
        {
            return $"追蹤球隊\n找不到「{teamInput}」這支球隊。你可以試試：兄弟、統一、樂天、味全、富邦、台鋼。";
        }

        var subscription = await dbContext.TelegramChatSubscriptions
            .FirstOrDefaultAsync(chat => chat.ChatId == chatId, cancellationToken);

        if (subscription is null)
        {
            return "追蹤球隊\n目前還找不到這個聊天紀錄，請先再傳一次訊息給 bot。";
        }

        subscription.FollowedTeamCode = teamCode;
        subscription.LastUpdatedTime = DateTimeOffset.UtcNow;
        await dbContext.SaveChangesAsync(cancellationToken);

        var teamName = CpblTeamCatalog.GetDisplayName(teamCode);
        return $"已開始追蹤 {teamName}\n之後我會優先幫你整理這隊的今日賽程、賽後重點和相關新聞。";
    }

    private async Task<string> BuildUnfollowReplyAsync(string? chatId, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(chatId))
        {
            return "取消追蹤\n這個功能需要直接在 Telegram 聊天視窗裡使用。";
        }

        var subscription = await dbContext.TelegramChatSubscriptions
            .FirstOrDefaultAsync(chat => chat.ChatId == chatId, cancellationToken);

        if (subscription is null || string.IsNullOrWhiteSpace(subscription.FollowedTeamCode))
        {
            return "取消追蹤\n你目前還沒有設定追蹤球隊。";
        }

        var previousTeamName = CpblTeamCatalog.GetDisplayName(subscription.FollowedTeamCode);
        subscription.FollowedTeamCode = null;
        subscription.LastUpdatedTime = DateTimeOffset.UtcNow;
        await dbContext.SaveChangesAsync(cancellationToken);

        return $"已取消追蹤 {previousTeamName}\n之後我會回到中立模式，不再優先帶這支球隊的視角。";
    }

    private async Task<string> BuildMyFollowReplyAsync(string? chatId, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(chatId))
        {
            return "我的追蹤\n這個功能需要直接在 Telegram 聊天視窗裡使用。";
        }

        var followedTeamCode = await GetFollowedTeamCodeAsync(chatId, cancellationToken);
        if (string.IsNullOrWhiteSpace(followedTeamCode))
        {
            return "我的追蹤\n你目前還沒有設定追蹤球隊，可以輸入 /follow 兄弟 或 /follow 樂天。";
        }

        return $"我的追蹤\n目前優先整理的是 {CpblTeamCatalog.GetDisplayName(followedTeamCode)}。";
    }

    private async Task<string?> GetFollowedTeamCodeAsync(string? chatId, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(chatId))
        {
            return null;
        }

        return await dbContext.TelegramChatSubscriptions
            .Where(chat => chat.ChatId == chatId)
            .Select(chat => chat.FollowedTeamCode)
            .FirstOrDefaultAsync(cancellationToken);
    }

    private static bool TryExtractTeamCommand(string normalizedCommand, out string teamCode, out TeamCommandKind commandKind)
    {
        teamCode = string.Empty;
        commandKind = TeamCommandKind.Summary;

        if (normalizedCommand.StartsWith("/team ", StringComparison.OrdinalIgnoreCase))
        {
            var rawTeam = normalizedCommand["/team ".Length..].Trim();
            if (CpblTeamCatalog.TryResolveTeamCode(rawTeam, out teamCode))
            {
                return true;
            }
        }

        if (!CpblTeamCatalog.TryResolveTeamCodeFromText(normalizedCommand, out teamCode, out _))
        {
            teamCode = string.Empty;
            return false;
        }

        if (normalizedCommand.Contains("明天", StringComparison.Ordinal) &&
            (normalizedCommand.Contains("有沒有打", StringComparison.Ordinal) ||
             normalizedCommand.Contains("比賽", StringComparison.Ordinal)))
        {
            commandKind = TeamCommandKind.TeamTomorrow;
            return true;
        }

        if (normalizedCommand.Contains("今天", StringComparison.Ordinal) &&
            (normalizedCommand.Contains("有沒有打", StringComparison.Ordinal) ||
             normalizedCommand.Contains("比賽", StringComparison.Ordinal)))
        {
            commandKind = TeamCommandKind.TeamToday;
            return true;
        }

        if (normalizedCommand.Contains("戰績", StringComparison.Ordinal) ||
            normalizedCommand.Contains("近況", StringComparison.Ordinal) ||
            normalizedCommand.Contains("最近", StringComparison.Ordinal))
        {
            commandKind = TeamCommandKind.Summary;
            return true;
        }

        if (normalizedCommand.StartsWith("/team", StringComparison.OrdinalIgnoreCase))
        {
            commandKind = TeamCommandKind.Summary;
            return true;
        }

        if (normalizedCommand.Contains("球隊", StringComparison.Ordinal))
        {
            commandKind = TeamCommandKind.Summary;
            return true;
        }

        return false;
    }

    private static bool IsTodayScheduleCommand(string normalizedCommand)
    {
        return normalizedCommand.Equals("/today", StringComparison.OrdinalIgnoreCase) ||
               (normalizedCommand.Contains("今天", StringComparison.Ordinal) &&
                (normalizedCommand.Contains("比賽", StringComparison.Ordinal) ||
                 normalizedCommand.Contains("賽程", StringComparison.Ordinal)));
    }

    private static bool IsTomorrowScheduleCommand(string normalizedCommand)
    {
        return normalizedCommand.Equals("/tomorrow", StringComparison.OrdinalIgnoreCase) ||
               normalizedCommand.Equals("明天", StringComparison.Ordinal) ||
               (normalizedCommand.Contains("明天", StringComparison.Ordinal) &&
                (normalizedCommand.Contains("比賽", StringComparison.Ordinal) ||
                 normalizedCommand.Contains("賽程", StringComparison.Ordinal)));
    }

    private static bool IsTodayBestCommand(string normalizedCommand)
    {
        return normalizedCommand.Equals("/today_best", StringComparison.OrdinalIgnoreCase) ||
               normalizedCommand.Equals("today best", StringComparison.OrdinalIgnoreCase) ||
               normalizedCommand.Contains("最值得看", StringComparison.Ordinal) ||
               normalizedCommand.Contains("今日焦點", StringComparison.Ordinal) ||
               normalizedCommand.Contains("本日焦點", StringComparison.Ordinal);
    }

    private static bool IsLatestNewsCommand(string normalizedCommand)
    {
        return normalizedCommand.Equals("/news", StringComparison.OrdinalIgnoreCase) ||
               normalizedCommand.Equals("最新新聞", StringComparison.Ordinal) ||
               normalizedCommand.Equals("新聞", StringComparison.Ordinal) ||
               normalizedCommand.Equals("最新消息", StringComparison.Ordinal);
    }

    private static bool IsRecapCommand(string normalizedCommand)
    {
        return normalizedCommand.Equals("/recap", StringComparison.OrdinalIgnoreCase) ||
               normalizedCommand.Contains("recap", StringComparison.OrdinalIgnoreCase) ||
               normalizedCommand.Contains("今日總結", StringComparison.Ordinal) ||
               normalizedCommand.Contains("今日回顧", StringComparison.Ordinal);
    }

    private static bool IsMyFollowCommand(string normalizedCommand)
    {
        return normalizedCommand.Equals("/my_follow", StringComparison.OrdinalIgnoreCase) ||
               normalizedCommand.Equals("我的追蹤", StringComparison.Ordinal) ||
               normalizedCommand.Equals("目前追蹤", StringComparison.Ordinal);
    }

    private static bool IsUnfollowCommand(string normalizedCommand)
    {
        return normalizedCommand.Equals("/unfollow", StringComparison.OrdinalIgnoreCase) ||
               normalizedCommand.StartsWith("/unfollow ", StringComparison.OrdinalIgnoreCase) ||
               normalizedCommand.Equals("取消追蹤", StringComparison.Ordinal);
    }

    private static bool IsFollowCommand(string normalizedCommand, out string teamInput)
    {
        teamInput = string.Empty;

        if (normalizedCommand.StartsWith("/follow ", StringComparison.OrdinalIgnoreCase))
        {
            teamInput = normalizedCommand["/follow ".Length..].Trim();
            return !string.IsNullOrWhiteSpace(teamInput);
        }

        if (normalizedCommand.StartsWith("追蹤 ", StringComparison.Ordinal))
        {
            teamInput = normalizedCommand["追蹤 ".Length..].Trim();
            return !string.IsNullOrWhiteSpace(teamInput);
        }

        return false;
    }

    private static bool IsHelpCommand(string normalizedCommand)
    {
        return normalizedCommand.Equals("/help", StringComparison.OrdinalIgnoreCase) ||
               normalizedCommand.Equals("help", StringComparison.OrdinalIgnoreCase) ||
               normalizedCommand.Equals("幫助", StringComparison.Ordinal);
    }

    private static string BuildTeamTemperatureText(CpblTeamSummary summary)
    {
        if (summary.RecentGamesCount == 0)
        {
            return "資料還不夠完整，先觀察下一場。";
        }

        var runDiff = summary.RecentRunsScoredAverage - summary.RecentRunsAllowedAverage;
        return (summary.RecentWins, runDiff) switch
        {
            (>= 4, >= 1.0m) => "近況偏熱，最近攻守都比較順。",
            (>= 3, >= 0.0m) => "狀態穩定，這段時間沒有明顯失速。",
            (<= 1, <= -1.0m) => "近況偏冷，得失分內容都還需要回穩。",
            _ => "起伏比較大，最近還在找節奏。"
        };
    }

    private static string BuildNeutralRecapLine(GameInfo game)
    {
        var awayName = CpblTeamCatalog.GetDisplayName(game.AwayTeamCode);
        var homeName = CpblTeamCatalog.GetDisplayName(game.HomeTeamCode);
        var scoreGap = Math.Abs((game.AwayScore ?? 0) - (game.HomeScore ?? 0));
        var story = scoreGap <= 1
            ? "一路咬得很近"
            : scoreGap >= 4
                ? "比分差拉得比較開"
                : "中段局數分出高下";

        return $"{awayName} {game.AwayScore}:{game.HomeScore} {homeName}，{story}。";
    }

    private static string BuildTrackedTeamRecapLine(GameInfo game, string trackedTeamCode)
    {
        var trackedTeamName = CpblTeamCatalog.GetDisplayName(trackedTeamCode);
        var didWin = IsTeamWin(game, trackedTeamCode);
        var tone = didWin ? "收下一勝" : "這場沒能拿下";
        return $"{BuildCompactScoreLine(game)}，{trackedTeamName}{tone}。";
    }

    private static bool IsTrackedTeamGame(GameInfo game, string teamCode)
    {
        return string.Equals(game.HomeTeamCode, teamCode, StringComparison.OrdinalIgnoreCase) ||
               string.Equals(game.AwayTeamCode, teamCode, StringComparison.OrdinalIgnoreCase);
    }

    private static string BuildCompactScoreLine(GameInfo game)
    {
        var awayName = CpblTeamCatalog.GetDisplayName(game.AwayTeamCode);
        var homeName = CpblTeamCatalog.GetDisplayName(game.HomeTeamCode);
        return $"{awayName} {game.AwayScore}:{game.HomeScore} {homeName}";
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

    private static bool IsTeamWin(GameInfo game, string teamCode)
    {
        if (!game.HomeScore.HasValue || !game.AwayScore.HasValue)
        {
            return false;
        }

        return (string.Equals(game.HomeTeamCode, teamCode, StringComparison.OrdinalIgnoreCase) && game.HomeScore > game.AwayScore) ||
               (string.Equals(game.AwayTeamCode, teamCode, StringComparison.OrdinalIgnoreCase) && game.AwayScore > game.HomeScore);
    }

    private static string BuildGameSummary(GameInfo game)
    {
        var awayName = CpblTeamCatalog.GetDisplayName(game.AwayTeamCode);
        var homeName = CpblTeamCatalog.GetDisplayName(game.HomeTeamCode);
        var timeText = game.StartTime?.ToString("HH:mm") ?? "--:--";
        var localizedStatus = BuildLocalizedStatus(game);
        var scoreText = ShouldDisplayScore(game, localizedStatus)
            ? $" | 比分 {game.AwayScore}:{game.HomeScore}"
            : string.Empty;

        return $"{game.GameDate:MM/dd} {timeText} {awayName} vs {homeName} | {localizedStatus}{scoreText} | {game.Venue ?? "待公告"}";
    }

    private static bool ShouldDisplayScore(GameInfo game, string? localizedStatus = null)
    {
        if (!game.AwayScore.HasValue || !game.HomeScore.HasValue)
        {
            return false;
        }

        localizedStatus ??= BuildLocalizedStatus(game);
        return !string.Equals(localizedStatus, "尚未開打", StringComparison.Ordinal);
    }

    private static string BuildHelpReply()
    {
        return """
你可以直接輸入指令：
- /today：今天賽程
- /today_best：今天最值得看的比賽
- /team 兄弟：球隊近況摘要
- /follow 兄弟：設定你想追的隊伍
- /my_follow：查看目前追蹤
- /recap：看今天打完的重點

如果不想記指令，也可以直接打中文：
- 想看今天賽程：今天有什麼比賽
- 想查某隊今天有沒有打：統一今天有沒有比賽
- 想看某隊近況：樂天最近怎麼樣
- 想看新聞：有什麼最新新聞
""";
    }

    private static string NormalizeCommand(string rawValue)
    {
        var cleanedValue = rawValue
            .Replace('，', ' ')
            .Replace('。', ' ')
            .Replace('、', ' ')
            .Replace('：', ' ')
            .Replace(':', ' ')
            .Replace('；', ' ')
            .Replace(';', ' ')
            .Replace('？', ' ')
            .Replace('?', ' ')
            .Replace('！', ' ')
            .Replace('!', ' ')
            .Replace('（', ' ')
            .Replace('）', ' ')
            .Replace('(', ' ')
            .Replace(')', ' ')
            .Replace('【', ' ')
            .Replace('】', ' ')
            .Replace('[', ' ')
            .Replace(']', ' ')
            .Replace('｜', ' ')
            .Replace('|', ' ')
            .Replace('／', ' ')
            .Replace('　', ' ');

        var normalized = string.Join(
            ' ',
            cleanedValue.Split(new[] { '\r', '\n', '\t', ' ' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));

        return Regex.Replace(normalized, @"\s+", " ").Trim();
    }

    private static DateOnly GetTaipeiToday()
    {
        return DateOnly.FromDateTime(GetTaipeiNow());
    }

    private static DateTime GetTaipeiNow()
    {
        return TimeZoneInfo.ConvertTimeBySystemTimeZoneId(DateTime.UtcNow, "Taipei Standard Time");
    }

    private async Task TryRefreshGameDateAsync(DateOnly targetDate, CancellationToken cancellationToken)
    {
        try
        {
            await cpblGameSyncService.SyncDateAsync(targetDate, cancellationToken);
        }
        catch (Exception exception)
        {
            logger.LogWarning(exception, "Game refresh failed before building command reply for {TargetDate}.", targetDate);
        }
    }

    private async Task TryRefreshNewsAsync(CancellationToken cancellationToken)
    {
        try
        {
            await baseballNewsSyncService.SyncAsync(cancellationToken);
        }
        catch (Exception exception)
        {
            logger.LogWarning(exception, "News refresh failed before building command reply.");
        }
    }

    private enum TeamCommandKind
    {
        Summary,
        TeamToday,
        TeamTomorrow
    }
}
