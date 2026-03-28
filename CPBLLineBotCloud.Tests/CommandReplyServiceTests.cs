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
        var myFollowReply = await service.BuildReplyAsync("/following", "chat-1");

        Assert.Contains("已開始追蹤 中信兄弟", followReply);
        Assert.Contains("中信兄弟", myFollowReply);
        Assert.Equal("CT", await dbContext.TelegramChatSubscriptions.Select(x => x.FollowedTeamCode).SingleAsync());
    }

    [Fact]
    public async Task TomorrowScheduleReply_UsesTomorrowHeading()
    {
        await using var dbContext = CreateDbContext();
        var tomorrow = GetTaipeiToday().AddDays(1);

        dbContext.Games.Add(new GameInfo
        {
            GameDate = tomorrow,
            StartTime = new TimeOnly(17, 5),
            AwayTeamCode = "FG",
            HomeTeamCode = "CT",
            Status = "Scheduled",
            Venue = "洲際",
            LastUpdatedTime = DateTimeOffset.UtcNow
        });

        await dbContext.SaveChangesAsync();

        var service = CreateService(dbContext);
        var reply = await service.BuildReplyAsync("/tomorrow");

        Assert.Contains("明日賽程", reply);
        Assert.Contains("富邦悍將 vs 中信兄弟", reply);
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
    public async Task NextReply_UsesFollowedTeamWhenNoTeamIsProvided()
    {
        await using var dbContext = CreateDbContext();
        var today = GetTaipeiToday();

        dbContext.TelegramChatSubscriptions.Add(new TelegramChatSubscription
        {
            ChatId = "chat-1",
            ChatTitle = "Ian",
            EnableSchedulePush = true,
            EnableNewsPush = true,
            FollowedTeamCode = "CT",
            CreatedTime = DateTimeOffset.UtcNow,
            LastUpdatedTime = DateTimeOffset.UtcNow
        });

        dbContext.Games.Add(new GameInfo
        {
            GameDate = today.AddDays(1),
            StartTime = new TimeOnly(18, 35),
            AwayTeamCode = "UL",
            HomeTeamCode = "CT",
            Status = "Scheduled",
            Venue = "洲際",
            LastUpdatedTime = DateTimeOffset.UtcNow
        });

        await dbContext.SaveChangesAsync();

        var service = CreateService(dbContext);
        var reply = await service.BuildReplyAsync("/next", "chat-1");

        Assert.Contains("中信兄弟 下一場", reply);
        Assert.Contains("主場對 統一7-ELEVEn獅", reply);
    }

    [Fact]
    public async Task PreviewReply_ShowsTodayMatchups()
    {
        await using var dbContext = CreateDbContext();
        var today = GetTaipeiToday();

        dbContext.Games.Add(new GameInfo
        {
            GameDate = today,
            StartTime = new TimeOnly(18, 35),
            AwayTeamCode = "CT",
            HomeTeamCode = "UL",
            Status = "Scheduled",
            Venue = "台南",
            LastUpdatedTime = DateTimeOffset.UtcNow
        });

        await dbContext.SaveChangesAsync();

        var service = CreateService(dbContext);
        var reply = await service.BuildReplyAsync("/preview");

        Assert.Contains("今日對戰", reply);
        Assert.Contains("中信兄弟 vs 統一7-ELEVEn獅", reply);
    }

    [Fact]
    public async Task PreviewReply_CanFilterByTeam()
    {
        await using var dbContext = CreateDbContext();
        var today = GetTaipeiToday();

        dbContext.Games.Add(new GameInfo
        {
            GameDate = today,
            StartTime = new TimeOnly(17, 5),
            AwayTeamCode = "FG",
            HomeTeamCode = "CT",
            Status = "Scheduled",
            Venue = "洲際",
            LastUpdatedTime = DateTimeOffset.UtcNow
        });

        await dbContext.SaveChangesAsync();

        var service = CreateService(dbContext);
        var reply = await service.BuildReplyAsync("/preview 兄弟");

        Assert.Contains("賽前預覽", reply);
        Assert.Contains("中信兄弟 |", reply);
        Assert.Contains("主場對 富邦悍將", reply);
    }

    [Fact]
    public async Task ResultReply_ShowsFinalGamesForToday()
    {
        await using var dbContext = CreateDbContext();
        var today = GetTaipeiToday();

        dbContext.Games.Add(new GameInfo
        {
            GameDate = today,
            StartTime = new TimeOnly(18, 35),
            AwayTeamCode = "CT",
            HomeTeamCode = "UL",
            AwayScore = 6,
            HomeScore = 4,
            Status = "Final",
            Venue = "台南",
            LastUpdatedTime = DateTimeOffset.UtcNow
        });

        await dbContext.SaveChangesAsync();

        var service = CreateService(dbContext);
        var reply = await service.BuildReplyAsync("/result");

        Assert.Contains("今日賽果", reply);
        Assert.Contains("中信兄弟 6:4 統一7-ELEVEn獅", reply);
    }

    [Fact]
    public async Task YesterdayReply_ShowsFinalGamesForYesterday()
    {
        await using var dbContext = CreateDbContext();
        var yesterday = GetTaipeiToday().AddDays(-1);

        dbContext.Games.Add(new GameInfo
        {
            GameDate = yesterday,
            StartTime = new TimeOnly(17, 5),
            AwayTeamCode = "FG",
            HomeTeamCode = "CT",
            AwayScore = 3,
            HomeScore = 5,
            Status = "Final",
            Venue = "洲際",
            LastUpdatedTime = DateTimeOffset.UtcNow
        });

        await dbContext.SaveChangesAsync();

        var service = CreateService(dbContext);
        var reply = await service.BuildReplyAsync("/yesterday");

        Assert.Contains("昨日賽果", reply);
        Assert.Contains("富邦悍將 3:5 中信兄弟", reply);
    }

    [Fact]
    public async Task StandingsReply_UsesOfficialStandingsClient()
    {
        await using var dbContext = CreateDbContext();
        var service = CreateService(
            dbContext,
            officialDataClient: new FakeOfficialDataClient
            {
                Standings =
                [
                    new CpblTeamStandingSnapshot
                    {
                        Rank = 1,
                        TeamCode = "CT",
                        TeamName = "中信兄弟",
                        GamesPlayed = 10,
                        Wins = 7,
                        Losses = 3,
                        Ties = 0,
                        WinningPercentage = 0.700m,
                        GamesBehindText = "-",
                        StreakText = "W3"
                    },
                    new CpblTeamStandingSnapshot
                    {
                        Rank = 2,
                        TeamCode = "UL",
                        TeamName = "統一7-ELEVEn獅",
                        GamesPlayed = 10,
                        Wins = 6,
                        Losses = 4,
                        Ties = 0,
                        WinningPercentage = 0.600m,
                        GamesBehindText = "1.0",
                        StreakText = "L1"
                    }
                ]
            });

        var reply = await service.BuildReplyAsync("/standings");

        Assert.Contains("目前排名", reply);
        Assert.Contains("1. 中信兄弟", reply);
        Assert.Contains("2. 統一7-ELEVEn獅", reply);
    }

    [Fact]
    public async Task NotifyReply_ShowsCurrentStatus()
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

        var service = CreateService(dbContext);
        var reply = await service.BuildReplyAsync("/notify", "chat-1");

        Assert.Contains("提醒設定", reply);
        Assert.Contains("目前追蹤: 中信兄弟", reply);
        Assert.Contains("比賽提醒: 開啟", reply);
        Assert.Contains("新聞提醒: 關閉", reply);
    }

    [Fact]
    public async Task NotifyReply_CanToggleNewsOff()
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
        var reply = await service.BuildReplyAsync("/notify news off", "chat-1");

        Assert.Contains("新聞提醒已關閉", reply);
        Assert.False(await dbContext.TelegramChatSubscriptions.Select(x => x.EnableNewsPush).SingleAsync());
    }

    [Fact]
    public async Task LiveReply_ShowsInProgressGames()
    {
        await using var dbContext = CreateDbContext();
        var today = GetTaipeiToday();

        dbContext.Games.Add(new GameInfo
        {
            GameDate = today,
            StartTime = new TimeOnly(18, 35),
            AwayTeamCode = "RA",
            HomeTeamCode = "WD",
            AwayScore = 3,
            HomeScore = 2,
            Status = "Live",
            InningText = "7局上",
            Venue = "天母",
            LastUpdatedTime = DateTimeOffset.UtcNow
        });

        await dbContext.SaveChangesAsync();

        var service = CreateService(dbContext);
        var reply = await service.BuildReplyAsync("/live");

        Assert.Contains("即時比分", reply);
        Assert.Contains("樂天桃猿 vs 味全龍", reply);
        Assert.Contains("進行中，7局上", reply);
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

    private static CommandReplyService CreateService(
        ApplicationDbContext dbContext,
        ICpblInsightService? insightService = null,
        ICpblOfficialDataClient? officialDataClient = null)
    {
        return new CommandReplyService(
            dbContext,
            new FakeGameSyncService(),
            new FakeNewsSyncService(),
            officialDataClient ?? new FakeOfficialDataClient(),
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

    private sealed class FakeOfficialDataClient : ICpblOfficialDataClient
    {
        public IReadOnlyList<CpblTeamStandingSnapshot> Standings { get; init; } = [];

        public Task<IReadOnlyList<CpblOfficialGameSnapshot>> GetGamesAsync(DateOnly targetDate, CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<CpblOfficialGameSnapshot>>([]);

        public Task<IReadOnlyList<CpblTeamStandingSnapshot>> GetStandingsAsync(CancellationToken cancellationToken = default)
            => Task.FromResult(Standings);

        public Task<CpblPlayerStatsResult?> GetPlayerStatsAsync(string playerName, CancellationToken cancellationToken = default)
            => Task.FromResult<CpblPlayerStatsResult?>(null);

        public Task<CpblMatchupResult?> GetMatchupAsync(string hitterName, string pitcherName, CancellationToken cancellationToken = default)
            => Task.FromResult<CpblMatchupResult?>(null);
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
