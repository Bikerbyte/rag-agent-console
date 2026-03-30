using System.Diagnostics;
using CPBLLineBotCloud.Data;
using CPBLLineBotCloud.Models;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace CPBLLineBotCloud.Pages;

public class IndexModel(ApplicationDbContext dbContext, IOptions<TelegramBotOptions> telegramBotOptions) : PageModel
{
    public int TeamCount { get; private set; }
    public int TodayGameCount { get; private set; }
    public int NewsCount { get; private set; }
    public int ChatCount { get; private set; }
    public int PushLogCount { get; private set; }
    public int SyncLogCount { get; private set; }
    public int CurrentProcessId { get; private set; }
    public DateTime ProcessStartTime { get; private set; }
    public string EnvironmentName { get; private set; } = string.Empty;
    public string CurrentBaseUrl { get; private set; } = string.Empty;
    public bool BotEnabled { get; private set; }
    public bool HasBotToken { get; private set; }
    public string LatestGameSyncText { get; private set; } = "還沒有賽程同步紀錄";
    public string LatestNewsSyncText { get; private set; } = "還沒有新聞同步紀錄";
    public string LatestReplyText { get; private set; } = "還沒有 bot 回覆紀錄";

    public async Task OnGetAsync()
    {
        var today = DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeBySystemTimeZoneId(DateTime.UtcNow, "Taipei Standard Time"));

        TeamCount = await dbContext.Teams.CountAsync();
        TodayGameCount = await dbContext.Games.CountAsync(game => game.GameDate == today);
        NewsCount = await dbContext.NewsItems.CountAsync();
        ChatCount = await dbContext.TelegramChatSubscriptions.CountAsync();
        PushLogCount = await dbContext.PushLogs.CountAsync();
        SyncLogCount = await dbContext.SyncJobLogs.CountAsync();

        var latestGameSync = await dbContext.SyncJobLogs
            .Where(log => log.JobName == "CpblGameSync")
            .OrderByDescending(log => log.StartTime)
            .FirstOrDefaultAsync();

        var latestNewsSync = await dbContext.SyncJobLogs
            .Where(log => log.JobName == "BaseballNewsSync")
            .OrderByDescending(log => log.StartTime)
            .FirstOrDefaultAsync();

        var latestReply = await dbContext.PushLogs
            .OrderByDescending(log => log.CreatedTime)
            .FirstOrDefaultAsync();

        CurrentProcessId = Environment.ProcessId;
        ProcessStartTime = Process.GetCurrentProcess().StartTime;
        EnvironmentName = Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT") ?? "Production";
        CurrentBaseUrl = $"{Request.Scheme}://{Request.Host}";
        BotEnabled = telegramBotOptions.Value.Enabled;
        HasBotToken = !string.IsNullOrWhiteSpace(telegramBotOptions.Value.BotToken);

        if (latestGameSync is not null)
        {
            LatestGameSyncText = $"{latestGameSync.StartTime.ToOffset(TimeSpan.FromHours(8)):yyyy/MM/dd HH:mm} | {latestGameSync.Message}";
        }

        if (latestNewsSync is not null)
        {
            LatestNewsSyncText = $"{latestNewsSync.StartTime.ToOffset(TimeSpan.FromHours(8)):yyyy/MM/dd HH:mm} | {latestNewsSync.Message}";
        }

        if (latestReply is not null)
        {
            LatestReplyText = $"{latestReply.CreatedTime.ToOffset(TimeSpan.FromHours(8)):yyyy/MM/dd HH:mm} | {latestReply.MessageTitle} | {(latestReply.IsSuccess ? "成功" : latestReply.ErrorMessage)}";
        }
    }
}
