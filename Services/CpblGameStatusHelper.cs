using CPBLLineBotCloud.Models;

namespace CPBLLineBotCloud.Services;

/// <summary>
/// 統一整理官方比賽狀態與前台顯示文字，避免未來日期的比賽被過早標成進行中。
/// </summary>
public static class CpblGameStatusHelper
{
    private static readonly TimeZoneInfo TaipeiTimeZone = TimeZoneInfo.FindSystemTimeZoneById("Taipei Standard Time");

    public static string NormalizeStoredStatus(
        string rawStatus,
        DateOnly gameDate,
        TimeOnly? startTime,
        string? inningText,
        DateTimeOffset utcNow)
    {
        if (!string.Equals(rawStatus, "Live", StringComparison.OrdinalIgnoreCase))
        {
            return rawStatus;
        }

        var taipeiNow = TimeZoneInfo.ConvertTime(utcNow, TaipeiTimeZone);
        var taipeiToday = DateOnly.FromDateTime(taipeiNow.DateTime);
        var taipeiCurrentTime = TimeOnly.FromDateTime(taipeiNow.DateTime);

        if (gameDate > taipeiToday)
        {
            return "Scheduled";
        }

        if (gameDate == taipeiToday &&
            string.IsNullOrWhiteSpace(inningText) &&
            startTime.HasValue &&
            startTime.Value > taipeiCurrentTime.AddMinutes(5))
        {
            return "Scheduled";
        }

        return "Live";
    }

    public static string BuildLocalizedStatus(GameInfo game, DateTimeOffset utcNow)
    {
        var normalizedStatus = NormalizeStoredStatus(game.Status, game.GameDate, game.StartTime, game.InningText, utcNow);

        return normalizedStatus switch
        {
            "Live" when !string.IsNullOrWhiteSpace(game.InningText) => $"進行中，{game.InningText}",
            "Live" => "進行中",
            "Final" => "終場",
            "Suspended" => "暫停或延賽",
            _ => "尚未開打"
        };
    }
}
