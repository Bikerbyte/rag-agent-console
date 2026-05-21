using System.Diagnostics;
using SecurityAdvisoryBot.Data;
using SecurityAdvisoryBot.Models;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace SecurityAdvisoryBot.Pages;

public class IndexModel(
    ApplicationDbContext dbContext,
    IOptions<TelegramBotOptions> telegramBotOptions,
    IOptions<AppRuntimeOptions> appRuntimeOptions) : PageModel
{
    public int AdvisoryCount { get; private set; }
    public int KnownExploitedCount { get; private set; }
    public int RagChunkCount { get; private set; }
    public int ChatCount { get; private set; }
    public int PushLogCount { get; private set; }
    public int SyncLogCount { get; private set; }
    public int ActiveNodeCount { get; private set; }
    public int PendingTelegramUpdateCount { get; private set; }
    public int CurrentProcessId { get; private set; }
    public DateTime ProcessStartTime { get; private set; }
    public string EnvironmentName { get; private set; } = string.Empty;
    public string CurrentBaseUrl { get; private set; } = string.Empty;
    public string InstanceName { get; private set; } = string.Empty;
    public bool BotEnabled { get; private set; }
    public bool HasBotToken { get; private set; }
    public string LatestAdvisorySyncText { get; private set; } = "還沒有 security advisory 同步紀錄";
    public string RagIndexText { get; private set; } = "還沒有 RAG chunk 索引資料";
    public string LatestReplyText { get; private set; } = "還沒有 bot 回覆紀錄";
    public string RuntimeSummaryText { get; private set; } = "還沒有節點狀態資料";

    public async Task OnGetAsync()
    {
        var activeThreshold = DateTimeOffset.UtcNow.AddSeconds(-45);

        AdvisoryCount = await dbContext.SecurityAdvisories.CountAsync();
        KnownExploitedCount = await dbContext.SecurityAdvisories.CountAsync(advisory => advisory.IsKnownExploited);
        RagChunkCount = await dbContext.SecurityAdvisoryChunks.CountAsync();
        ChatCount = await dbContext.TelegramChatSubscriptions.CountAsync();
        PushLogCount = await dbContext.PushLogs.CountAsync();
        SyncLogCount = await dbContext.SyncJobLogs.CountAsync();
        ActiveNodeCount = await dbContext.RuntimeNodeHeartbeats.CountAsync(item => item.LastSeenTime >= activeThreshold);
        PendingTelegramUpdateCount = await dbContext.TelegramUpdateInboxes.CountAsync(item => item.Status == "Pending" || item.Status == "Processing");

        var latestAdvisorySync = await dbContext.SyncJobLogs
            .Where(log => log.JobName == "SecurityAdvisorySync")
            .OrderByDescending(log => log.StartTime)
            .FirstOrDefaultAsync();

        var latestReply = await dbContext.PushLogs
            .OrderByDescending(log => log.CreatedTime)
            .FirstOrDefaultAsync();

        CurrentProcessId = Environment.ProcessId;
        ProcessStartTime = Process.GetCurrentProcess().StartTime;
        EnvironmentName = Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT") ?? "Production";
        CurrentBaseUrl = $"{Request.Scheme}://{Request.Host}";
        InstanceName = appRuntimeOptions.Value.InstanceName;
        BotEnabled = telegramBotOptions.Value.Enabled;
        HasBotToken = !string.IsNullOrWhiteSpace(telegramBotOptions.Value.BotToken);

        if (latestAdvisorySync is not null)
        {
            LatestAdvisorySyncText = $"{latestAdvisorySync.StartTime.ToOffset(TimeSpan.FromHours(8)):yyyy/MM/dd HH:mm} | {latestAdvisorySync.Message}";
        }

        RagIndexText = $"{RagChunkCount} chunks indexed for lightweight RAG retrieval";

        if (latestReply is not null)
        {
            LatestReplyText = $"{latestReply.CreatedTime.ToOffset(TimeSpan.FromHours(8)):yyyy/MM/dd HH:mm} | {latestReply.MessageTitle} | {(latestReply.IsSuccess ? "成功" : latestReply.ErrorMessage)}";
        }

        RuntimeSummaryText = $"{InstanceName} | 線上節點 {ActiveNodeCount} 台 | Update queue 待處理 {PendingTelegramUpdateCount} 筆";
    }
}
