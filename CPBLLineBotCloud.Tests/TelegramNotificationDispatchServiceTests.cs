using CPBLLineBotCloud.Data;
using CPBLLineBotCloud.Models;
using CPBLLineBotCloud.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace CPBLLineBotCloud.Tests;

public class TelegramNotificationDispatchServiceTests
{
    [Fact]
    public async Task DailySummary_UsesTrackedTeamWhenConfigured()
    {
        await using var dbContext = CreateDbContext();
        dbContext.TelegramChatSubscriptions.Add(new TelegramChatSubscription
        {
            ChatId = "chat-1",
            ChatTitle = "Ian",
            EnableSchedulePush = true,
            EnableNewsPush = false,
            FollowedTeamCode = "CT",
            CreatedTime = DateTimeOffset.UtcNow,
            LastUpdatedTime = DateTimeOffset.UtcNow
        });
        await dbContext.SaveChangesAsync();

        var pushService = new RecordingPushService(dbContext);
        var service = CreateService(
            dbContext,
            pushService,
            new PushNotificationOptions
            {
                Enabled = true,
                EnableDailySummary = true,
                EnableGameStartPush = false,
                EnableNewsPush = false,
                EnableGameFinalPush = false,
                DailySummaryHour = 11,
                DailySummaryMinute = 0
            },
            new FixedTimeProvider(new DateTimeOffset(2026, 3, 27, 4, 0, 0, TimeSpan.Zero)),
            new FakeInsightService
            {
                TeamSummary = new CpblTeamSummary
                {
                    TeamCode = "CT",
                    TeamName = "中信兄弟",
                    NextGame = new GameInfo
                    {
                        GameDate = new DateOnly(2026, 3, 27),
                        StartTime = new TimeOnly(18, 35),
                        AwayTeamCode = "UL",
                        HomeTeamCode = "CT",
                        Status = "Scheduled",
                        Venue = "台中洲際",
                        LastUpdatedTime = DateTimeOffset.UtcNow
                    }
                },
                DailyFocus = new CpblDailyFocus
                {
                    FocusDate = new DateOnly(2026, 3, 27),
                    Items = ["今日推薦：中信兄弟 vs 統一7-ELEVEn獅"]
                }
            });

        await service.ProcessPendingNotificationsAsync();

        Assert.Single(pushService.Messages);
        Assert.Contains("你追蹤的 中信兄弟 今天有比賽", pushService.Messages[0].Body);
    }

    [Fact]
    public async Task NewsPush_RespectsTrackedTeamFilter()
    {
        await using var dbContext = CreateDbContext();
        dbContext.TelegramChatSubscriptions.AddRange(
            new TelegramChatSubscription
            {
                ChatId = "chat-ct",
                ChatTitle = "CT",
                EnableSchedulePush = false,
                EnableNewsPush = true,
                FollowedTeamCode = "CT",
                CreatedTime = DateTimeOffset.UtcNow,
                LastUpdatedTime = DateTimeOffset.UtcNow
            },
            new TelegramChatSubscription
            {
                ChatId = "chat-all",
                ChatTitle = "All",
                EnableSchedulePush = false,
                EnableNewsPush = true,
                CreatedTime = DateTimeOffset.UtcNow,
                LastUpdatedTime = DateTimeOffset.UtcNow
            });

        dbContext.NewsItems.AddRange(
            new NewsInfo
            {
                Title = "中信兄弟打線回穩",
                SourceName = "CPBL Official Site",
                Url = "https://example.test/ct-news",
                PublishTime = new DateTimeOffset(2026, 3, 27, 2, 0, 0, TimeSpan.Zero),
                Summary = "兄弟相關快訊",
                IsSent = false
            },
            new NewsInfo
            {
                Title = "富邦悍將牛棚調度整理",
                SourceName = "CPBL Official Site",
                Url = "https://example.test/fg-news",
                PublishTime = new DateTimeOffset(2026, 3, 27, 3, 0, 0, TimeSpan.Zero),
                Summary = "富邦相關快訊",
                IsSent = false
            });

        await dbContext.SaveChangesAsync();

        var pushService = new RecordingPushService(dbContext);
        var service = CreateService(
            dbContext,
            pushService,
            new PushNotificationOptions
            {
                Enabled = true,
                EnableDailySummary = false,
                EnableGameStartPush = false,
                EnableNewsPush = true,
                EnableGameFinalPush = false,
                NewsLookbackHours = 48
            },
            new FixedTimeProvider(new DateTimeOffset(2026, 3, 27, 4, 0, 0, TimeSpan.Zero)));

        await service.ProcessPendingNotificationsAsync();

        Assert.Contains(pushService.Messages, item => item.ChatId == "chat-ct" && item.Title == "中信兄弟打線回穩");
        Assert.DoesNotContain(pushService.Messages, item => item.ChatId == "chat-ct" && item.Title == "富邦悍將牛棚調度整理");
        Assert.Contains(pushService.Messages, item => item.ChatId == "chat-all" && item.Title == "中信兄弟打線回穩");
        Assert.Contains(pushService.Messages, item => item.ChatId == "chat-all" && item.Title == "富邦悍將牛棚調度整理");
    }

    [Fact]
    public async Task FinalPush_OnlySendsTrackedTeamGameWhenConfigured()
    {
        await using var dbContext = CreateDbContext();
        dbContext.TelegramChatSubscriptions.AddRange(
            new TelegramChatSubscription
            {
                ChatId = "chat-ct",
                ChatTitle = "CT",
                EnableSchedulePush = true,
                EnableNewsPush = false,
                FollowedTeamCode = "CT",
                CreatedTime = DateTimeOffset.UtcNow,
                LastUpdatedTime = DateTimeOffset.UtcNow
            },
            new TelegramChatSubscription
            {
                ChatId = "chat-all",
                ChatTitle = "All",
                EnableSchedulePush = true,
                EnableNewsPush = false,
                CreatedTime = DateTimeOffset.UtcNow,
                LastUpdatedTime = DateTimeOffset.UtcNow
            });

        dbContext.Games.AddRange(
            new GameInfo
            {
                GameDate = new DateOnly(2026, 3, 27),
                StartTime = new TimeOnly(18, 35),
                AwayTeamCode = "CT",
                HomeTeamCode = "UL",
                AwayScore = 3,
                HomeScore = 1,
                Status = "Final",
                Venue = "台南",
                LastUpdatedTime = new DateTimeOffset(2026, 3, 27, 12, 0, 0, TimeSpan.Zero)
            },
            new GameInfo
            {
                GameDate = new DateOnly(2026, 3, 27),
                StartTime = new TimeOnly(17, 5),
                AwayTeamCode = "RA",
                HomeTeamCode = "WD",
                AwayScore = 2,
                HomeScore = 4,
                Status = "Final",
                Venue = "天母",
                LastUpdatedTime = new DateTimeOffset(2026, 3, 27, 11, 30, 0, TimeSpan.Zero)
            });

        await dbContext.SaveChangesAsync();

        var pushService = new RecordingPushService(dbContext);
        var service = CreateService(
            dbContext,
            pushService,
            new PushNotificationOptions
            {
                Enabled = true,
                EnableDailySummary = false,
                EnableGameStartPush = false,
                EnableNewsPush = false,
                EnableGameFinalPush = true,
                FinalLookbackHours = 24
            },
            new FixedTimeProvider(new DateTimeOffset(2026, 3, 27, 12, 30, 0, TimeSpan.Zero)));

        await service.ProcessPendingNotificationsAsync();

        Assert.Contains(pushService.Messages, item => item.ChatId == "chat-ct" && item.Title.Contains("中信兄弟 vs 統一7-ELEVEn獅"));
        Assert.DoesNotContain(pushService.Messages, item => item.ChatId == "chat-ct" && item.Title.Contains("樂天桃猿 vs 味全龍"));
        Assert.Contains(pushService.Messages, item => item.ChatId == "chat-all" && item.Title.Contains("樂天桃猿 vs 味全龍"));
    }

    [Fact]
    public async Task GameStartPush_SendsPregameReminderForTrackedTeam()
    {
        await using var dbContext = CreateDbContext();
        dbContext.TelegramChatSubscriptions.Add(new TelegramChatSubscription
        {
            ChatId = "chat-ct",
            ChatTitle = "CT",
            EnableSchedulePush = true,
            EnableNewsPush = false,
            FollowedTeamCode = "CT",
            CreatedTime = DateTimeOffset.UtcNow,
            LastUpdatedTime = DateTimeOffset.UtcNow
        });

        dbContext.Games.Add(new GameInfo
        {
            GameDate = new DateOnly(2026, 3, 27),
            StartTime = new TimeOnly(18, 35),
            AwayTeamCode = "UL",
            HomeTeamCode = "CT",
            Status = "Scheduled",
            Venue = "洲際",
            LastUpdatedTime = new DateTimeOffset(2026, 3, 27, 9, 0, 0, TimeSpan.Zero)
        });

        await dbContext.SaveChangesAsync();

        var pushService = new RecordingPushService(dbContext);
        var service = CreateService(
            dbContext,
            pushService,
            new PushNotificationOptions
            {
                Enabled = true,
                EnableDailySummary = false,
                EnableGameStartPush = true,
                EnableNewsPush = false,
                EnableGameFinalPush = false,
                GameStartLeadMinutes = 30
            },
            new FixedTimeProvider(new DateTimeOffset(2026, 3, 27, 10, 10, 0, TimeSpan.Zero)));

        await service.ProcessPendingNotificationsAsync();

        Assert.Single(pushService.Messages);
        Assert.Equal("GameStart", pushService.Messages[0].PushType);
        Assert.Contains("開賽提醒", pushService.Messages[0].Title);
        Assert.Contains("你追蹤的 中信兄弟 即將在 25 分鐘後開打", pushService.Messages[0].Body);
    }

    private static TelegramNotificationDispatchService CreateService(
        ApplicationDbContext dbContext,
        RecordingPushService pushService,
        PushNotificationOptions options,
        TimeProvider timeProvider,
        ICpblInsightService? insightService = null)
    {
        return new TelegramNotificationDispatchService(
            dbContext,
            insightService ?? new FakeInsightService(),
            pushService,
            Options.Create(options),
            timeProvider,
            NullLogger<TelegramNotificationDispatchService>.Instance);
    }

    private static ApplicationDbContext CreateDbContext()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options;

        return new ApplicationDbContext(options);
    }

    private sealed class RecordingPushService(ApplicationDbContext dbContext) : ITelegramPushService
    {
        public List<RecordedMessage> Messages { get; } = [];

        public async Task<bool> SendPushAsync(string chatId, string messageTitle, string messageBody, string pushType, CancellationToken cancellationToken = default)
        {
            Messages.Add(new RecordedMessage(chatId, messageTitle, messageBody, pushType));

            dbContext.PushLogs.Add(new PushLog
            {
                TargetGroupId = chatId,
                MessageTitle = messageTitle,
                PushType = pushType,
                IsSuccess = true,
                CreatedTime = DateTimeOffset.UtcNow
            });

            await dbContext.SaveChangesAsync(cancellationToken);
            return true;
        }
    }

    private sealed record RecordedMessage(string ChatId, string Title, string Body, string PushType);

    private sealed class FakeInsightService : ICpblInsightService
    {
        public CpblTeamSummary? TeamSummary { get; init; }
        public CpblDailyFocus DailyFocus { get; init; } = new();

        public Task<CpblPlayerStatsResult?> GetPlayerStatsAsync(string playerName, CancellationToken cancellationToken = default)
            => Task.FromResult<CpblPlayerStatsResult?>(null);

        public Task<CpblMatchupResult?> GetMatchupAsync(string hitterName, string pitcherName, CancellationToken cancellationToken = default)
            => Task.FromResult<CpblMatchupResult?>(null);

        public Task<CpblTeamSummary?> GetTeamSummaryAsync(string teamCode, CancellationToken cancellationToken = default)
            => Task.FromResult(TeamSummary);

        public Task<CpblDailyFocus> GetDailyFocusAsync(CancellationToken cancellationToken = default)
            => Task.FromResult(DailyFocus);

        public Task<CpblGameRecommendation?> GetTodayBestGameAsync(CancellationToken cancellationToken = default)
            => Task.FromResult<CpblGameRecommendation?>(null);

        public Task<IReadOnlyList<CpblOfficialGameSnapshot>> GetRecentHighlightsAsync(int lookbackDays, CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<CpblOfficialGameSnapshot>>([]);

        public Task<IReadOnlyList<CpblScorePrediction>> GetPredictionsAsync(DateOnly targetDate, string? awayTeamCode = null, string? homeTeamCode = null, CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<CpblScorePrediction>>([]);
    }

    private sealed class FixedTimeProvider(DateTimeOffset timestamp) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => timestamp;
    }
}
