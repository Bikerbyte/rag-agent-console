using CPBLLineBotCloud.Data;
using CPBLLineBotCloud.Models;
using CPBLLineBotCloud.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace CPBLLineBotCloud.Tests;

public class CommandReplyServiceTests
{
    [Fact]
    public async Task TodayScheduleReply_UsesReadableTeamNames()
    {
        await using var dbContext = CreateDbContext();
        var today = GetTaipeiToday();

        dbContext.Games.Add(new GameInfo
        {
            GameDate = today,
            StartTime = new TimeOnly(18, 35),
            AwayTeamCode = "CT",
            HomeTeamCode = "UL",
            AwayScore = 5,
            HomeScore = 4,
            Status = "Final",
            Venue = "台中洲際",
            LastUpdatedTime = DateTimeOffset.UtcNow
        });

        await dbContext.SaveChangesAsync();

        var service = CreateService(dbContext);
        var reply = await service.BuildReplyAsync("/today");

        Assert.Contains("中信兄弟 vs 統一7-ELEVEn獅", reply);
        Assert.Contains("比分: 中信兄弟 5 : 4 統一7-ELEVEn獅", reply);
    }

    [Fact]
    public async Task FollowAndMyFollow_UpdateChatPreference()
    {
        await using var dbContext = CreateDbContext();
        dbContext.TelegramChatSubscriptions.Add(new TelegramChatSubscription
        {
            ChatId = "chat-1",
            ChatTitle = "Ian",
            EnableSchedulePush = true,
            EnableNewsPush = true,
            CreatedTime = DateTimeOffset.UtcNow,
            LastUpdatedTime = DateTimeOffset.UtcNow
        });
        await dbContext.SaveChangesAsync();

        var service = CreateService(dbContext);

        var followReply = await service.BuildReplyAsync("/follow 兄弟", "chat-1");
        var myFollowReply = await service.BuildReplyAsync("/my_follow", "chat-1");

        Assert.Contains("已開始追蹤 中信兄弟", followReply);
        Assert.Contains("中信兄弟", myFollowReply);
        Assert.Equal("CT", await dbContext.TelegramChatSubscriptions.Select(x => x.FollowedTeamCode).SingleAsync());
    }

    [Fact]
    public async Task TodayBestReply_UsesRecommendationService()
    {
        await using var dbContext = CreateDbContext();
        var service = CreateService(
            dbContext,
            new FakeInsightService
            {
                TodayBest = new CpblGameRecommendation
                {
                    FocusDate = new DateOnly(2026, 3, 27),
                    GameLabel = "中信兄弟 vs 統一7-ELEVEn獅",
                    StartTimeText = "18:35",
                    VenueText = "台南",
                    Reasons =
                    [
                        "兩隊最近都不是冷手，近期戰績有支撐。",
                        "近況接近，這場比較像有機會一路咬到後段。"
                    ]
                }
            });

        var reply = await service.BuildReplyAsync("/today_best");

        Assert.Contains("今日最值得看", reply);
        Assert.Contains("中信兄弟 vs 統一7-ELEVEn獅", reply);
        Assert.Contains("推薦理由", reply);
    }

    [Fact]
    public async Task TeamReply_UsesNeutralSummaryInsteadOfFixedTeam()
    {
        await using var dbContext = CreateDbContext();
        var service = CreateService(
            dbContext,
            new FakeInsightService
            {
                TeamSummary = new CpblTeamSummary
                {
                    TeamCode = "RA",
                    TeamName = "樂天桃猿",
                    RecentGamesCount = 5,
                    RecentWins = 4,
                    RecentLosses = 1,
                    RecentRunsScoredAverage = 5.4m,
                    RecentRunsAllowedAverage = 3.1m,
                    RecentTrendText = "勝 勝 敗 勝 勝"
                }
            });

        var reply = await service.BuildReplyAsync("/team 樂天");

        Assert.Contains("樂天桃猿近況", reply);
        Assert.Contains("近 5 場: 4 勝 1 敗", reply);
        Assert.Contains("狀態判讀", reply);
    }

    [Fact]
    public async Task TeamReply_AcceptsFullTeamNameInput()
    {
        await using var dbContext = CreateDbContext();
        var service = CreateService(
            dbContext,
            new FakeInsightService
            {
                TeamSummary = new CpblTeamSummary
                {
                    TeamCode = "FG",
                    TeamName = "富邦悍將",
                    RecentGamesCount = 5,
                    RecentWins = 3,
                    RecentLosses = 2,
                    RecentRunsScoredAverage = 4.8m,
                    RecentRunsAllowedAverage = 4.1m,
                    RecentTrendText = "勝 敗 勝 勝 敗"
                }
            });

        var reply = await service.BuildReplyAsync("/team 富邦悍將");

        Assert.Contains("富邦悍將近況", reply);
        Assert.DoesNotContain("你可以直接輸入指令", reply);
    }

    [Fact]
    public async Task RecapReply_PrioritizesFollowedTeamWhenAvailable()
    {
        await using var dbContext = CreateDbContext();
        var today = GetTaipeiToday();

        dbContext.TelegramChatSubscriptions.Add(new TelegramChatSubscription
        {
            ChatId = "chat-1",
            ChatTitle = "Ian",
            EnableSchedulePush = true,
            EnableNewsPush = true,
            FollowedTeamCode = "UL",
            CreatedTime = DateTimeOffset.UtcNow,
            LastUpdatedTime = DateTimeOffset.UtcNow
        });

        dbContext.Games.Add(new GameInfo
        {
            GameDate = today,
            StartTime = new TimeOnly(18, 35),
            AwayTeamCode = "CT",
            HomeTeamCode = "UL",
            AwayScore = 2,
            HomeScore = 4,
            Status = "Final",
            Venue = "台南",
            LastUpdatedTime = DateTimeOffset.UtcNow
        });

        await dbContext.SaveChangesAsync();

        var service = CreateService(dbContext);
        var reply = await service.BuildReplyAsync("/recap", "chat-1");

        Assert.Contains("你追蹤的球隊", reply);
        Assert.Contains("統一7-ELEVEn獅收下一勝", reply);
    }

    private static CommandReplyService CreateService(ApplicationDbContext dbContext, ICpblInsightService? insightService = null)
    {
        return new CommandReplyService(
            dbContext,
            new FakeGameSyncService(),
            new FakeNewsSyncService(),
            insightService ?? new FakeInsightService(),
            NullLogger<CommandReplyService>.Instance);
    }

    private static ApplicationDbContext CreateDbContext()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options;

        return new ApplicationDbContext(options);
    }

    private static DateOnly GetTaipeiToday()
    {
        return DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeBySystemTimeZoneId(DateTime.UtcNow, "Taipei Standard Time"));
    }

    private sealed class FakeGameSyncService : ICpblGameSyncService
    {
        public Task<int> SyncAsync(CancellationToken cancellationToken = default) => Task.FromResult(0);
        public Task<int> SyncDateAsync(DateOnly targetDate, CancellationToken cancellationToken = default) => Task.FromResult(0);
    }

    private sealed class FakeNewsSyncService : IBaseballNewsSyncService
    {
        public Task<int> SyncAsync(CancellationToken cancellationToken = default) => Task.FromResult(0);
    }

    private sealed class FakeInsightService : ICpblInsightService
    {
        public CpblTeamSummary? TeamSummary { get; init; }
        public CpblGameRecommendation? TodayBest { get; init; }

        public Task<CpblPlayerStatsResult?> GetPlayerStatsAsync(string playerName, CancellationToken cancellationToken = default)
            => Task.FromResult<CpblPlayerStatsResult?>(null);

        public Task<CpblMatchupResult?> GetMatchupAsync(string hitterName, string pitcherName, CancellationToken cancellationToken = default)
            => Task.FromResult<CpblMatchupResult?>(null);

        public Task<CpblTeamSummary?> GetTeamSummaryAsync(string teamCode, CancellationToken cancellationToken = default)
            => Task.FromResult(TeamSummary);

        public Task<CpblDailyFocus> GetDailyFocusAsync(CancellationToken cancellationToken = default)
            => Task.FromResult(new CpblDailyFocus());

        public Task<CpblGameRecommendation?> GetTodayBestGameAsync(CancellationToken cancellationToken = default)
            => Task.FromResult(TodayBest);

        public Task<IReadOnlyList<CpblOfficialGameSnapshot>> GetRecentHighlightsAsync(int lookbackDays, CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<CpblOfficialGameSnapshot>>([]);

        public Task<IReadOnlyList<CpblScorePrediction>> GetPredictionsAsync(DateOnly targetDate, string? awayTeamCode = null, string? homeTeamCode = null, CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<CpblScorePrediction>>([]);
    }
}
