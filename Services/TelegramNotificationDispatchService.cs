using System.Text;
using CPBLLineBotCloud.Data;
using CPBLLineBotCloud.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace CPBLLineBotCloud.Services;

/// <summary>
/// 依照排程推播規則，為各個已訂閱的 chat 決定要送哪些 Telegram 訊息。
/// </summary>
public class TelegramNotificationDispatchService(
    ApplicationDbContext dbContext,
    ICpblInsightService cpblInsightService,
    ITelegramPushService telegramPushService,
    IOptions<PushNotificationOptions> pushOptions,
    TimeProvider timeProvider,
    ILogger<TelegramNotificationDispatchService> logger) : ITelegramNotificationDispatchService
{
    private static readonly TimeZoneInfo TaipeiTimeZone = TimeZoneInfo.FindSystemTimeZoneById("Taipei Standard Time");

    public async Task ProcessPendingNotificationsAsync(CancellationToken cancellationToken = default)
    {
        // 同一輪 worker 會處理多種 push，但每一種都靠 PushLog 自己做重送判斷。
        var options = pushOptions.Value;
        if (!options.Enabled)
        {
            logger.LogDebug("Telegram push notifications are disabled in configuration.");
            return;
        }

        var subscriptions = await dbContext.TelegramChatSubscriptions
            .OrderBy(chat => chat.ChatTitle)
            .ToListAsync(cancellationToken);

        if (subscriptions.Count == 0)
        {
            logger.LogDebug("No Telegram chat subscriptions are configured for auto push.");
            return;
        }

        var taipeiNow = TimeZoneInfo.ConvertTime(timeProvider.GetUtcNow(), TaipeiTimeZone);

        if (options.EnableDailySummary)
        {
            await DispatchDailySummaryAsync(subscriptions, taipeiNow, options, cancellationToken);
        }

        if (options.EnableGameStartPush)
        {
            await DispatchGameStartPushAsync(subscriptions, taipeiNow, options, cancellationToken);
        }

        if (options.EnableLiveScorePush)
        {
            await DispatchLiveScorePushAsync(subscriptions, taipeiNow, cancellationToken);
        }

        if (options.EnableNewsPush)
        {
            await DispatchNewsPushAsync(subscriptions, taipeiNow, options, cancellationToken);
        }

        if (options.EnableGameFinalPush)
        {
            await DispatchGameFinalPushAsync(subscriptions, taipeiNow, options, cancellationToken);
        }
    }

    private async Task DispatchDailySummaryAsync(
        IReadOnlyList<TelegramChatSubscription> subscriptions,
        DateTimeOffset taipeiNow,
        PushNotificationOptions options,
        CancellationToken cancellationToken)
    {
        if (taipeiNow.Hour < options.DailySummaryHour ||
            (taipeiNow.Hour == options.DailySummaryHour && taipeiNow.Minute < options.DailySummaryMinute))
        {
            return;
        }

        var today = DateOnly.FromDateTime(taipeiNow.DateTime);
        var summaryTitle = $"每日摘要 {today:yyyy/MM/dd}";
        var dailyFocus = await cpblInsightService.GetDailyFocusAsync(cancellationToken);

        foreach (var subscription in subscriptions.Where(chat => chat.EnableSchedulePush))
        {
            var alreadySent = await HasSuccessfulPushAsync(subscription.ChatId, "DailySummary", summaryTitle, cancellationToken);
            if (alreadySent)
            {
                continue;
            }

            var messageBody = await BuildDailySummaryBodyAsync(subscription, today, dailyFocus, cancellationToken);
            await telegramPushService.SendPushAsync(subscription.ChatId, summaryTitle, messageBody, "DailySummary", cancellationToken);
        }
    }

    private async Task DispatchNewsPushAsync(
        IReadOnlyList<TelegramChatSubscription> subscriptions,
        DateTimeOffset taipeiNow,
        PushNotificationOptions options,
        CancellationToken cancellationToken)
    {
        // 新聞推播要看一段回溯時間，因為同步和推播 worker 不一定剛好同一分鐘執行。
        var cutoff = taipeiNow.AddHours(-Math.Max(1, options.NewsLookbackHours)).ToUniversalTime();

        var pendingNews = await dbContext.NewsItems
            .Where(news => !news.IsSent && news.PublishTime >= cutoff)
            .OrderBy(news => news.PublishTime)
            .ToListAsync(cancellationToken);

        foreach (var news in pendingNews)
        {
            var eligibleSubscriptions = subscriptions
                .Where(chat => chat.EnableNewsPush &&
                               (string.IsNullOrWhiteSpace(chat.FollowedTeamCode) || IsRelatedNews(news, chat.FollowedTeamCode)))
                .ToList();

            if (eligibleSubscriptions.Count == 0)
            {
                news.IsSent = true;
                continue;
            }

            var allDelivered = true;
            foreach (var subscription in eligibleSubscriptions)
            {
                var alreadySent = await HasSuccessfulPushAsync(subscription.ChatId, "NewsPush", news.Title, cancellationToken);
                if (alreadySent)
                {
                    continue;
                }

                var messageBody = BuildNewsPushBody(news, subscription.FollowedTeamCode);
                var isSuccess = await telegramPushService.SendPushAsync(subscription.ChatId, news.Title, messageBody, "NewsPush", cancellationToken);
                allDelivered &= isSuccess;
            }

            if (allDelivered)
            {
                news.IsSent = true;
            }
        }

        await dbContext.SaveChangesAsync(cancellationToken);
    }

    private async Task DispatchGameStartPushAsync(
        IReadOnlyList<TelegramChatSubscription> subscriptions,
        DateTimeOffset taipeiNow,
        PushNotificationOptions options,
        CancellationToken cancellationToken)
    {
        // 開賽提醒是近時間提醒，不是拿來做長期賽程通知。
        var leadMinutes = Math.Max(5, options.GameStartLeadMinutes);
        var today = DateOnly.FromDateTime(taipeiNow.DateTime);

        var candidateGames = await dbContext.Games
            .Where(game =>
                (game.GameDate == today || game.GameDate == today.AddDays(1)) &&
                game.StartTime.HasValue &&
                game.Status != "Final" &&
                game.Status != "Suspended")
            .OrderBy(game => game.GameDate)
            .ThenBy(game => game.StartTime)
            .ToListAsync(cancellationToken);

        foreach (var game in candidateGames)
        {
            var scheduledTime = BuildTaipeiGameTime(game);
            if (scheduledTime is null)
            {
                continue;
            }

            if (scheduledTime <= taipeiNow || scheduledTime > taipeiNow.AddMinutes(leadMinutes))
            {
                continue;
            }

            var startTitle = BuildGameStartPushTitle(game);
            var minutesLeft = Math.Max(1, (int)Math.Ceiling((scheduledTime.Value - taipeiNow).TotalMinutes));

            foreach (var subscription in subscriptions.Where(chat =>
                         chat.EnableSchedulePush &&
                         !string.IsNullOrWhiteSpace(chat.FollowedTeamCode)))
            {
                if (!IsTrackedTeamGame(game, subscription.FollowedTeamCode!))
                {
                    continue;
                }

                var alreadySent = await HasSuccessfulPushAsync(subscription.ChatId, "GameStart", startTitle, cancellationToken);
                if (alreadySent)
                {
                    continue;
                }

                var messageBody = BuildGameStartPushBody(game, subscription.FollowedTeamCode!, minutesLeft);
                await telegramPushService.SendPushAsync(subscription.ChatId, startTitle, messageBody, "GameStart", cancellationToken);
            }
        }
    }

    private async Task DispatchGameFinalPushAsync(
        IReadOnlyList<TelegramChatSubscription> subscriptions,
        DateTimeOffset taipeiNow,
        PushNotificationOptions options,
        CancellationToken cancellationToken)
    {
        var cutoff = taipeiNow.AddHours(-Math.Max(1, options.FinalLookbackHours)).ToUniversalTime();
        var today = DateOnly.FromDateTime(taipeiNow.DateTime);

        var finalGames = await dbContext.Games
            .Where(game =>
                game.Status == "Final" &&
                game.HomeScore.HasValue &&
                game.AwayScore.HasValue &&
                (game.LastUpdatedTime >= cutoff || game.GameDate >= today.AddDays(-1)))
            .OrderBy(game => game.GameDate)
            .ThenBy(game => game.StartTime)
            .ToListAsync(cancellationToken);

        foreach (var game in finalGames)
        {
            var finalTitle = BuildFinalPushTitle(game);

            foreach (var subscription in subscriptions.Where(chat => chat.EnableSchedulePush))
            {
                if (!string.IsNullOrWhiteSpace(subscription.FollowedTeamCode) &&
                    !IsTrackedTeamGame(game, subscription.FollowedTeamCode))
                {
                    continue;
                }

                var alreadySent = await HasSuccessfulPushAsync(subscription.ChatId, "GameFinal", finalTitle, cancellationToken);
                if (alreadySent)
                {
                    continue;
                }

                var messageBody = BuildFinalPushBody(game, subscription.FollowedTeamCode);
                await telegramPushService.SendPushAsync(subscription.ChatId, finalTitle, messageBody, "GameFinal", cancellationToken);
            }
        }
    }

    private async Task DispatchLiveScorePushAsync(
        IReadOnlyList<TelegramChatSubscription> subscriptions,
        DateTimeOffset taipeiNow,
        CancellationToken cancellationToken)
    {
        // 只有比分真的有變動才送 live push，避免每輪 worker 都吵一次群組。
        var today = DateOnly.FromDateTime(taipeiNow.DateTime);
        var liveGames = await dbContext.Games
            .Where(game =>
                game.Status == "Live" &&
                game.GameDate >= today.AddDays(-1) &&
                game.HomeScore.HasValue &&
                game.AwayScore.HasValue &&
                game.PreviousHomeScore.HasValue &&
                game.PreviousAwayScore.HasValue)
            .OrderBy(game => game.GameDate)
            .ThenBy(game => game.StartTime)
            .ToListAsync(cancellationToken);

        foreach (var game in liveGames)
        {
            if (!HasScoreChanged(game))
            {
                continue;
            }

            var updateTitle = BuildLiveScorePushTitle(game);

            foreach (var subscription in subscriptions.Where(chat =>
                         chat.EnableSchedulePush &&
                         !string.IsNullOrWhiteSpace(chat.FollowedTeamCode)))
            {
                if (!IsTrackedTeamGame(game, subscription.FollowedTeamCode!))
                {
                    continue;
                }

                var alreadySent = await HasSuccessfulPushAsync(subscription.ChatId, "GameLiveUpdate", updateTitle, cancellationToken);
                if (alreadySent)
                {
                    continue;
                }

                var messageBody = BuildLiveScorePushBody(game, subscription.FollowedTeamCode!);
                await telegramPushService.SendPushAsync(subscription.ChatId, updateTitle, messageBody, "GameLiveUpdate", cancellationToken);
            }
        }
    }

    private async Task<bool> HasSuccessfulPushAsync(
        string chatId,
        string pushType,
        string messageTitle,
        CancellationToken cancellationToken)
    {
        return await dbContext.PushLogs.AnyAsync(
            log => log.TargetGroupId == chatId &&
                   log.PushType == pushType &&
                   log.MessageTitle == messageTitle &&
                   log.IsSuccess,
            cancellationToken);
    }

    private async Task<string> BuildDailySummaryBodyAsync(
        TelegramChatSubscription subscription,
        DateOnly targetDate,
        CpblDailyFocus dailyFocus,
        CancellationToken cancellationToken)
    {
        var replyBuilder = new StringBuilder();
        replyBuilder.AppendLine($"{targetDate:yyyy/MM/dd} 主動整理");

        if (!string.IsNullOrWhiteSpace(subscription.FollowedTeamCode))
        {
            var teamSummary = await cpblInsightService.GetTeamSummaryAsync(subscription.FollowedTeamCode, cancellationToken);
            replyBuilder.AppendLine(BuildTrackedTeamDailyLine(targetDate, subscription.FollowedTeamCode, teamSummary));
            replyBuilder.AppendLine(string.Empty);
        }

        foreach (var item in dailyFocus.Items.Take(3))
        {
            replyBuilder.AppendLine($"- {item}");
        }

        return replyBuilder.ToString().TrimEnd();
    }

    private static string BuildTrackedTeamDailyLine(DateOnly targetDate, string teamCode, CpblTeamSummary? summary)
    {
        var teamName = CpblTeamCatalog.GetDisplayName(teamCode);

        if (summary?.NextGame is { } nextGame && nextGame.GameDate == targetDate)
        {
            var opponentCode = nextGame.HomeTeamCode == teamCode ? nextGame.AwayTeamCode : nextGame.HomeTeamCode;
            var opponentName = CpblTeamCatalog.GetDisplayName(opponentCode);
            var venue = string.IsNullOrWhiteSpace(nextGame.Venue) ? "待公告" : nextGame.Venue;
            var timeText = nextGame.StartTime?.ToString("HH:mm") ?? "--:--";
            var homeAwayText = nextGame.HomeTeamCode == teamCode ? "主場" : "客場";
            return $"你追蹤的 {teamName} 今天有比賽: {timeText} {homeAwayText}對 {opponentName}，地點 {venue}";
        }

        if (summary?.LatestGame is { } latestGame && latestGame.GameDate == targetDate)
        {
            return $"你追蹤的 {teamName} 今天已完賽: {BuildCompactFinalLine(latestGame)}";
        }

        return $"你追蹤的 {teamName} 今天沒有排到比賽";
    }

    private static string BuildNewsPushBody(NewsInfo news, string? followedTeamCode)
    {
        var lines = new List<string>();

        if (!string.IsNullOrWhiteSpace(followedTeamCode) && IsRelatedNews(news, followedTeamCode))
        {
            lines.Add($"你追蹤的 {CpblTeamCatalog.GetDisplayName(followedTeamCode)} 有新消息");
        }

        lines.Add($"時間: {news.PublishTime.ToOffset(TimeSpan.FromHours(8)):MM/dd HH:mm}");

        if (!string.IsNullOrWhiteSpace(news.Summary))
        {
            lines.Add($"摘要: {news.Summary}");
        }

        lines.Add($"來源: {news.SourceName}");
        lines.Add(news.Url);
        return string.Join('\n', lines);
    }

    private static string BuildFinalPushTitle(GameInfo game)
    {
        var awayName = CpblTeamCatalog.GetDisplayName(game.AwayTeamCode);
        var homeName = CpblTeamCatalog.GetDisplayName(game.HomeTeamCode);
        return $"終場快報 {game.GameDate:MM/dd} {awayName} vs {homeName}";
    }

    private static string BuildGameStartPushTitle(GameInfo game)
    {
        var awayName = CpblTeamCatalog.GetDisplayName(game.AwayTeamCode);
        var homeName = CpblTeamCatalog.GetDisplayName(game.HomeTeamCode);
        return $"開賽提醒 {game.GameDate:MM/dd} {awayName} vs {homeName}";
    }

    private static string BuildLiveScorePushTitle(GameInfo game)
    {
        var awayName = CpblTeamCatalog.GetDisplayName(game.AwayTeamCode);
        var homeName = CpblTeamCatalog.GetDisplayName(game.HomeTeamCode);
        return $"戰況更新 {game.GameDate:MM/dd} {awayName} vs {homeName} {game.AwayScore}:{game.HomeScore}";
    }

    private static string BuildGameStartPushBody(GameInfo game, string trackedTeamCode, int minutesLeft)
    {
        var trackedTeamName = CpblTeamCatalog.GetDisplayName(trackedTeamCode);
        var opponentCode = string.Equals(game.HomeTeamCode, trackedTeamCode, StringComparison.OrdinalIgnoreCase)
            ? game.AwayTeamCode
            : game.HomeTeamCode;
        var opponentName = CpblTeamCatalog.GetDisplayName(opponentCode);
        var homeAwayText = string.Equals(game.HomeTeamCode, trackedTeamCode, StringComparison.OrdinalIgnoreCase) ? "主場" : "客場";
        var startTimeText = game.StartTime?.ToString("HH:mm") ?? "--:--";
        var venueText = string.IsNullOrWhiteSpace(game.Venue) ? "待公告" : game.Venue;

        return $"你追蹤的 {trackedTeamName} 即將在 {minutesLeft} 分鐘後開打\n{game.GameDate:MM/dd} {startTimeText} {homeAwayText}對 {opponentName}\n地點: {venueText}";
    }

    private static string BuildLiveScorePushBody(GameInfo game, string trackedTeamCode)
    {
        var trackedTeamName = CpblTeamCatalog.GetDisplayName(trackedTeamCode);
        var awayName = CpblTeamCatalog.GetDisplayName(game.AwayTeamCode);
        var homeName = CpblTeamCatalog.GetDisplayName(game.HomeTeamCode);
        var highlights = BuildLiveHighlights(game, awayName, homeName);
        var lines = new List<string>
        {
            $"你追蹤的 {trackedTeamName} 有新戰況",
            $"目前比分: {awayName} {game.AwayScore}:{game.HomeScore} {homeName}"
        };

        if (highlights.Count > 0)
        {
            lines.Add($"重點: {string.Join(" / ", highlights)}");
        }

        lines.Add($"狀態: {BuildLiveStatusText(game)}");

        if (!string.IsNullOrWhiteSpace(game.Venue))
        {
            lines.Add($"地點: {game.Venue}");
        }

        return string.Join('\n', lines);
    }

    private static string BuildFinalPushBody(GameInfo game, string? followedTeamCode)
    {
        var lines = new List<string>
        {
            BuildCompactFinalLine(game)
        };

        if (!string.IsNullOrWhiteSpace(followedTeamCode) && IsTrackedTeamGame(game, followedTeamCode))
        {
            lines.Add(BuildTrackedTeamResultLine(game, followedTeamCode));
        }

        if (!string.IsNullOrWhiteSpace(game.Venue))
        {
            lines.Add($"地點: {game.Venue}");
        }

        return string.Join('\n', lines);
    }

    private static string BuildCompactFinalLine(GameInfo game)
    {
        var awayName = CpblTeamCatalog.GetDisplayName(game.AwayTeamCode);
        var homeName = CpblTeamCatalog.GetDisplayName(game.HomeTeamCode);
        return $"{awayName} {game.AwayScore}:{game.HomeScore} {homeName}";
    }

    private static string BuildTrackedTeamResultLine(GameInfo game, string trackedTeamCode)
    {
        var teamName = CpblTeamCatalog.GetDisplayName(trackedTeamCode);
        var didWin =
            (game.HomeTeamCode == trackedTeamCode && game.HomeScore > game.AwayScore) ||
            (game.AwayTeamCode == trackedTeamCode && game.AwayScore > game.HomeScore);

        return didWin ? $"{teamName} 收下這場比賽" : $"{teamName} 這場沒能拿下";
    }

    private static bool IsTrackedTeamGame(GameInfo game, string teamCode)
    {
        return string.Equals(game.HomeTeamCode, teamCode, StringComparison.OrdinalIgnoreCase) ||
               string.Equals(game.AwayTeamCode, teamCode, StringComparison.OrdinalIgnoreCase);
    }

    private static bool HasScoreChanged(GameInfo game)
    {
        return game.AwayScore != game.PreviousAwayScore || game.HomeScore != game.PreviousHomeScore;
    }

    private static List<string> BuildLiveHighlights(GameInfo game, string awayName, string homeName)
    {
        var highlights = new List<string>();
        var awayDelta = (game.AwayScore ?? 0) - (game.PreviousAwayScore ?? 0);
        var homeDelta = (game.HomeScore ?? 0) - (game.PreviousHomeScore ?? 0);

        if (awayDelta > 0)
        {
            highlights.Add($"{awayName} 攻下 {awayDelta} 分");
        }

        if (homeDelta > 0)
        {
            highlights.Add($"{homeName} 攻下 {homeDelta} 分");
        }

        var leadChangeText = BuildLeadChangeText(game, awayName, homeName);
        if (!string.IsNullOrWhiteSpace(leadChangeText))
        {
            highlights.Add(leadChangeText);
        }

        return highlights;
    }

    private static string? BuildLeadChangeText(GameInfo game, string awayName, string homeName)
    {
        var previousState = GetLeadState(game.PreviousAwayScore ?? 0, game.PreviousHomeScore ?? 0);
        var currentState = GetLeadState(game.AwayScore ?? 0, game.HomeScore ?? 0);

        if (previousState == currentState)
        {
            return null;
        }

        return (previousState, currentState) switch
        {
            (LeadState.Tied, LeadState.AwayLead) => $"{awayName} 取得領先",
            (LeadState.Tied, LeadState.HomeLead) => $"{homeName} 取得領先",
            (LeadState.AwayLead, LeadState.Tied) => $"{homeName} 追平比數",
            (LeadState.HomeLead, LeadState.Tied) => $"{awayName} 追平比數",
            (LeadState.AwayLead, LeadState.HomeLead) => $"{homeName} 逆轉超前",
            (LeadState.HomeLead, LeadState.AwayLead) => $"{awayName} 逆轉超前",
            _ => null
        };
    }

    private static LeadState GetLeadState(int awayScore, int homeScore)
    {
        if (awayScore == homeScore)
        {
            return LeadState.Tied;
        }

        return awayScore > homeScore ? LeadState.AwayLead : LeadState.HomeLead;
    }

    private static string BuildLiveStatusText(GameInfo game)
    {
        if (!string.IsNullOrWhiteSpace(game.InningText))
        {
            return $"進行中，{game.InningText}";
        }

        return "進行中";
    }

    private static DateTimeOffset? BuildTaipeiGameTime(GameInfo game)
    {
        if (!game.StartTime.HasValue)
        {
            return null;
        }

        var localTime = game.GameDate.ToDateTime(game.StartTime.Value);
        var offset = TaipeiTimeZone.GetUtcOffset(localTime);
        return new DateTimeOffset(localTime, offset);
    }

    private static bool IsRelatedNews(NewsInfo news, string teamCode)
    {
        var content = $"{news.Title}\n{news.Summary}\n{news.Category}";
        return CpblTeamCatalog.GetSearchKeywords(teamCode)
            .Any(keyword => content.Contains(keyword, StringComparison.OrdinalIgnoreCase));
    }

    private enum LeadState
    {
        Tied,
        AwayLead,
        HomeLead
    }
}
