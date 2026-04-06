using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using CPBLLineBotCloud.Data;
using CPBLLineBotCloud.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace CPBLLineBotCloud.Services;

/// <summary>
/// 將官方 CPBL 賽程整理成 <see cref="GameInfo"/>，供指令回覆、管理頁面與推播流程共用。
/// </summary>
public partial class CpblGameSyncService(
    ApplicationDbContext dbContext,
    IHttpClientFactory httpClientFactory,
    IOptions<DataSourceOptions> dataSourceOptions,
    IOptions<AppRuntimeOptions> runtimeOptions,
    ILogger<CpblGameSyncService> logger) : ICpblGameSyncService
{
    private static readonly TimeSpan FreshnessWindow = TimeSpan.FromMinutes(5);
    private readonly DataSourceOptions dataSourceOptions = dataSourceOptions.Value;

    public Task<int> SyncAsync(CancellationToken cancellationToken = default)
    {
        var localNow = TimeZoneInfo.ConvertTimeBySystemTimeZoneId(DateTime.UtcNow, "Taipei Standard Time");
        var targetDate = DateOnly.FromDateTime(localNow);
        return SyncDateAsync(targetDate, cancellationToken);
    }

    public async Task<int> SyncDateAsync(DateOnly targetDate, CancellationToken cancellationToken = default)
    {
        var startedAt = DateTimeOffset.UtcNow;

        // 短時間內如果多個流程都來抓同一天，這裡先擋掉重複同步，避免一直打官方站。
        if (await HasRecentSuccessfulSyncAsync(targetDate, cancellationToken))
        {
            logger.LogInformation("Skipping CPBL game sync because a recent successful sync already exists for {TargetDate}.", targetDate);
            return 0;
        }

        logger.LogInformation("Starting CPBL game sync from official CPBL source for {TargetDate}.", targetDate);

        try
        {
            var officialGames = await FetchOfficialGamesAsync(targetDate, cancellationToken);
            await EnsureTeamDirectoryAsync(cancellationToken);

            var existingGames = await dbContext.Games
                .Where(game => game.GameDate == targetDate)
                .ToListAsync(cancellationToken);

            var matchedGameIds = new HashSet<int>();

            foreach (var officialGame in officialGames)
            {
                var existingGame = FindMatchingGame(existingGames, officialGame);
                if (existingGame is null)
                {
                    dbContext.Games.Add(officialGame);
                    continue;
                }

                matchedGameIds.Add(existingGame.GameInfoId);
                ApplyOfficialSnapshot(existingGame, officialGame);
            }

            var staleGames = existingGames
                .Where(game => !matchedGameIds.Contains(game.GameInfoId))
                .ToList();

            if (staleGames.Count > 0)
            {
                dbContext.Games.RemoveRange(staleGames);
            }

            dbContext.SyncJobLogs.Add(new SyncJobLog
            {
                InstanceName = runtimeOptions.Value.InstanceName,
                JobName = "CpblGameSync",
                StartTime = startedAt,
                EndTime = DateTimeOffset.UtcNow,
                IsSuccess = true,
                Message = $"Official CPBL schedule sync completed for {targetDate:yyyy-MM-dd}. {officialGames.Count} game(s) stored."
            });

            await dbContext.SaveChangesAsync(cancellationToken);
            logger.LogInformation("Finished CPBL game sync for {TargetDate}. Stored {GameCount} game(s).", targetDate, officialGames.Count);

            return officialGames.Count;
        }
        catch (Exception exception)
        {
            logger.LogError(exception, "CPBL game sync failed for {TargetDate}.", targetDate);

            dbContext.SyncJobLogs.Add(new SyncJobLog
            {
                InstanceName = runtimeOptions.Value.InstanceName,
                JobName = "CpblGameSync",
                StartTime = startedAt,
                EndTime = DateTimeOffset.UtcNow,
                IsSuccess = false,
                Message = $"Official CPBL schedule sync failed for {targetDate:yyyy-MM-dd}: {exception.Message}"
            });

            await dbContext.SaveChangesAsync(cancellationToken);
            throw;
        }
    }

    private async Task<bool> HasRecentSuccessfulSyncAsync(DateOnly targetDate, CancellationToken cancellationToken)
    {
        var threshold = DateTimeOffset.UtcNow - FreshnessWindow;
        var expectedMessageTag = targetDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

        return await dbContext.SyncJobLogs
            .Where(log => log.JobName == "CpblGameSync" && log.IsSuccess && log.StartTime >= threshold)
            .AnyAsync(log => log.Message != null && log.Message.Contains(expectedMessageTag), cancellationToken);
    }

    private async Task<List<GameInfo>> FetchOfficialGamesAsync(DateOnly targetDate, CancellationToken cancellationToken)
    {
        using var httpClient = httpClientFactory.CreateClient();

        // 官方賽程 API 需要先拿首頁上的 anti-forgery token 才能正常呼叫。
        var homePageResponse = await httpClient.GetStringAsync(dataSourceOptions.CpblScheduleBaseUrl, cancellationToken);
        var antiForgeryToken = ExtractAntiForgeryToken(homePageResponse);

        if (string.IsNullOrWhiteSpace(antiForgeryToken))
        {
            throw new InvalidOperationException("Unable to find the CPBL anti-forgery token on the official homepage.");
        }

        using var request = new HttpRequestMessage(HttpMethod.Post, $"{dataSourceOptions.CpblScheduleBaseUrl.TrimEnd('/')}/home/getdetaillist")
        {
            Content = new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["__RequestVerificationToken"] = antiForgeryToken,
                ["GameDate"] = targetDate.ToString("yyyy/MM/dd", CultureInfo.InvariantCulture),
                ["GameSno"] = string.Empty,
                ["KindCode"] = string.Empty
            })
        };

        request.Headers.Referrer = new Uri(dataSourceOptions.CpblScheduleBaseUrl);
        request.Headers.Add("X-Requested-With", "XMLHttpRequest");

        using var response = await httpClient.SendAsync(request, cancellationToken);
        response.EnsureSuccessStatusCode();

        await using var responseStream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var document = await JsonDocument.ParseAsync(responseStream, cancellationToken: cancellationToken);

        if (!document.RootElement.TryGetProperty("Success", out var successProperty) || !successProperty.GetBoolean())
        {
            throw new InvalidOperationException("Official CPBL schedule endpoint returned an unsuccessful payload.");
        }

        if (!document.RootElement.TryGetProperty("GameADetailJson", out var gamesJsonProperty))
        {
            return [];
        }

        var gamesJson = gamesJsonProperty.GetString();
        if (string.IsNullOrWhiteSpace(gamesJson))
        {
            return [];
        }

        using var gamesDocument = JsonDocument.Parse(gamesJson);
        var results = new List<GameInfo>();

        foreach (var gameElement in gamesDocument.RootElement.EnumerateArray())
        {
            var preExeDate = ParseNullableDateTime(gameElement, "PreExeDate");
            var localGameDateTime = preExeDate.HasValue
                ? TimeZoneInfo.ConvertTimeBySystemTimeZoneId(preExeDate.Value.UtcDateTime, "Taipei Standard Time")
                : new DateTimeOffset(targetDate.Year, targetDate.Month, targetDate.Day, 18, 35, 0, TimeSpan.Zero);

            var awayTeamName = GetString(gameElement, "VisitingTeamName");
            var homeTeamName = GetString(gameElement, "HomeTeamName");

            results.Add(new GameInfo
            {
                GameDate = DateOnly.FromDateTime(localGameDateTime.DateTime),
                StartTime = TimeOnly.FromDateTime(localGameDateTime.DateTime),
                AwayTeamCode = MapTeamCode(awayTeamName),
                HomeTeamCode = MapTeamCode(homeTeamName),
                AwayScore = GetNullableInt(gameElement, "VisitingTotalScore") ?? GetNullableInt(gameElement, "VisitingScore"),
                HomeScore = GetNullableInt(gameElement, "HomeTotalScore") ?? GetNullableInt(gameElement, "HomeScore"),
                Status = BuildStatusText(gameElement),
                InningText = BuildInningText(gameElement),
                Venue = GetString(gameElement, "FieldAbbe"),
                LastUpdatedTime = DateTimeOffset.UtcNow
            });
        }

        return results;
    }

    private async Task EnsureTeamDirectoryAsync(CancellationToken cancellationToken)
    {
        // 先把基本球隊資料補齊，後面管理頁和訊息組裝才不會缺顯示名稱。
        foreach (var team in OfficialTeams.Values)
        {
            var exists = await dbContext.Teams.AnyAsync(existingTeam => existingTeam.TeamCode == team.TeamCode, cancellationToken);
            if (exists)
            {
                continue;
            }

            dbContext.Teams.Add(new TeamInfo
            {
                TeamCode = team.TeamCode,
                TeamName = team.TeamName,
                DisplayName = team.DisplayName
            });
        }
    }

    private static GameInfo? FindMatchingGame(IReadOnlyList<GameInfo> existingGames, GameInfo officialGame)
    {
        var sameTeams = existingGames
            .Where(game =>
                string.Equals(game.HomeTeamCode, officialGame.HomeTeamCode, StringComparison.OrdinalIgnoreCase) &&
                string.Equals(game.AwayTeamCode, officialGame.AwayTeamCode, StringComparison.OrdinalIgnoreCase))
            .ToList();

        if (sameTeams.Count == 0)
        {
            return null;
        }

        if (officialGame.StartTime.HasValue)
        {
            var exactTimeMatch = sameTeams.FirstOrDefault(game => game.StartTime == officialGame.StartTime);
            if (exactTimeMatch is not null)
            {
                return exactTimeMatch;
            }
        }

        return sameTeams[0];
    }

    private static void ApplyOfficialSnapshot(GameInfo existingGame, GameInfo officialGame)
    {
        existingGame.PreviousAwayScore = existingGame.AwayScore;
        existingGame.PreviousHomeScore = existingGame.HomeScore;
        existingGame.PreviousStatus = existingGame.Status;
        existingGame.GameDate = officialGame.GameDate;
        existingGame.StartTime = officialGame.StartTime;
        existingGame.AwayTeamCode = officialGame.AwayTeamCode;
        existingGame.HomeTeamCode = officialGame.HomeTeamCode;
        existingGame.AwayScore = officialGame.AwayScore;
        existingGame.HomeScore = officialGame.HomeScore;
        existingGame.Status = officialGame.Status;
        existingGame.InningText = officialGame.InningText;
        existingGame.Venue = officialGame.Venue;
        existingGame.LastUpdatedTime = officialGame.LastUpdatedTime;
    }

    private static string BuildStatusText(JsonElement gameElement)
    {
        var gameStatus = GetNullableInt(gameElement, "GameStatus");
        var presentStatus = GetNullableInt(gameElement, "PresentStatus");

        if (gameStatus == 3)
        {
            return "Final";
        }

        var isGameStop = GetString(gameElement, "IsGameStop");
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

        return BuildStatusText(gameElement) == "Scheduled" ? "尚未開打" : null;
    }

    private static DateTimeOffset? ParseNullableDateTime(JsonElement element, string propertyName)
    {
        var value = GetString(element, propertyName);
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        if (DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.AssumeLocal, out var dateTimeOffset))
        {
            return dateTimeOffset.ToUniversalTime();
        }

        return null;
    }

    private static int? GetNullableInt(JsonElement element, string propertyName)
    {
        if (!element.TryGetProperty(propertyName, out var property))
        {
            return null;
        }

        return property.ValueKind switch
        {
            JsonValueKind.Number when property.TryGetInt32(out var value) => value,
            JsonValueKind.String when int.TryParse(property.GetString(), out var value) => value,
            _ => null
        };
    }

    private static string? GetString(JsonElement element, string propertyName)
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

    private static string ExtractAntiForgeryToken(string html)
    {
        var match = AntiForgeryTokenRegex().Match(html);
        return match.Success ? match.Groups["token"].Value : string.Empty;
    }

    private static string MapTeamCode(string? teamName)
    {
        if (!string.IsNullOrWhiteSpace(teamName) && OfficialTeams.TryGetValue(teamName.Trim(), out var team))
        {
            return team.TeamCode;
        }

        return teamName?.Trim() ?? "UNK";
    }

    [GeneratedRegex("name=\"__RequestVerificationToken\"\\s+type=\"hidden\"\\s+value=\"(?<token>[^\"]+)\"", RegexOptions.IgnoreCase)]
    private static partial Regex AntiForgeryTokenRegex();

    private static readonly IReadOnlyDictionary<string, TeamDirectoryEntry> OfficialTeams =
        new Dictionary<string, TeamDirectoryEntry>(StringComparer.Ordinal)
        {
            ["富邦悍將"] = new("FG", "Fubon Guardians", "富邦悍將"),
            ["中信兄弟"] = new("CT", "CTBC Brothers", "中信兄弟"),
            ["統一7-ELEVEn獅"] = new("UL", "Uni-Lions", "統一7-ELEVEn獅"),
            ["樂天桃猿"] = new("RA", "Rakuten Monkeys", "樂天桃猿"),
            ["味全龍"] = new("WD", "Wei Chuan Dragons", "味全龍"),
            ["台鋼雄鷹"] = new("TS", "TSG Hawks", "台鋼雄鷹")
        };

    private sealed record TeamDirectoryEntry(string TeamCode, string TeamName, string DisplayName);
}
