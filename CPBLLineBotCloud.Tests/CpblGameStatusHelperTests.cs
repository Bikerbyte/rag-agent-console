using CPBLLineBotCloud.Models;
using CPBLLineBotCloud.Services;
using Xunit;

namespace CPBLLineBotCloud.Tests;

public class CpblGameStatusHelperTests
{
    [Fact]
    public void NormalizeStoredStatus_ForFutureGame_KeepsScheduled()
    {
        var normalizedStatus = CpblGameStatusHelper.NormalizeStoredStatus(
            "Live",
            new DateOnly(2026, 4, 7),
            new TimeOnly(18, 35),
            null,
            new DateTimeOffset(2026, 4, 6, 15, 0, 0, TimeSpan.Zero));

        Assert.Equal("Scheduled", normalizedStatus);
    }

    [Fact]
    public void BuildLocalizedStatus_ForSameDayFutureStartWithoutInning_ShowsScheduled()
    {
        var game = new GameInfo
        {
            GameDate = new DateOnly(2026, 4, 6),
            StartTime = new TimeOnly(23, 30),
            AwayTeamCode = "CT",
            HomeTeamCode = "FG",
            Status = "Live",
            LastUpdatedTime = DateTimeOffset.UtcNow
        };

        var localizedStatus = CpblGameStatusHelper.BuildLocalizedStatus(
            game,
            new DateTimeOffset(2026, 4, 6, 15, 0, 0, TimeSpan.Zero));

        Assert.Equal("尚未開打", localizedStatus);
    }

    [Fact]
    public void BuildLocalizedStatus_ForLiveGameWithInning_ShowsLiveText()
    {
        var game = new GameInfo
        {
            GameDate = new DateOnly(2026, 4, 6),
            StartTime = new TimeOnly(18, 35),
            AwayTeamCode = "CT",
            HomeTeamCode = "FG",
            Status = "Live",
            InningText = "第 3 局",
            LastUpdatedTime = DateTimeOffset.UtcNow
        };

        var localizedStatus = CpblGameStatusHelper.BuildLocalizedStatus(
            game,
            new DateTimeOffset(2026, 4, 6, 11, 30, 0, TimeSpan.Zero));

        Assert.Equal("進行中，第 3 局", localizedStatus);
    }
}
