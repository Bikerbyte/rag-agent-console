using SecurityAdvisoryBot.Data;
using SecurityAdvisoryBot.Models;
using SecurityAdvisoryBot.Services;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace SecurityAdvisoryBot.Pages;

public class IndexModel(
    ApplicationDbContext dbContext,
    IOptions<TelegramBotOptions> telegramBotOptions,
    IOptions<AppRuntimeOptions> appRuntimeOptions,
    IOptions<AiProviderOptions> aiProviderOptions,
    IAppSettingsService appSettingsService) : PageModel
{
    public string AgentName { get; private set; } = "AI Assistant";
    public string AgentTagline { get; private set; } = "Knowledge-grounded AI agent";
    public int AdvisoryCount { get; private set; }
    public int KnownExploitedCount { get; private set; }
    public int RagChunkCount { get; private set; }
    public int ChatCount { get; private set; }
    public int ActiveNodeCount { get; private set; }
    public int PendingTelegramUpdateCount { get; private set; }
    public string EnvironmentName { get; private set; } = string.Empty;
    public string InstanceName { get; private set; } = string.Empty;
    public bool BotEnabled { get; private set; }
    public bool HasBotToken { get; private set; }
    public string AiProviderText { get; private set; } = string.Empty;
    public string LatestAdvisorySyncText { get; private set; } = "No security advisory sync log yet.";
    public string RagIndexText { get; private set; } = "No RAG chunks indexed yet.";
    public string LatestReplyText { get; private set; } = "No Telegram delivery log yet.";

    public async Task OnGetAsync(CancellationToken cancellationToken)
    {
        var agentOptions = await appSettingsService.GetAgentOptionsAsync(cancellationToken);
        AgentName = agentOptions.AgentName;
        AgentTagline = agentOptions.AgentTagline;

        var activeThreshold = DateTimeOffset.UtcNow.AddSeconds(-45);

        AdvisoryCount = await dbContext.SecurityAdvisories.CountAsync();
        KnownExploitedCount = await dbContext.SecurityAdvisories.CountAsync(advisory => advisory.IsKnownExploited);
        RagChunkCount = await dbContext.SecurityAdvisoryChunks.CountAsync();
        ChatCount = await dbContext.TelegramChatSubscriptions.CountAsync();
        ActiveNodeCount = await dbContext.RuntimeNodeHeartbeats.CountAsync(item => item.LastSeenTime >= activeThreshold);
        PendingTelegramUpdateCount = await dbContext.TelegramUpdateInboxes.CountAsync(item => item.Status == "Pending" || item.Status == "Processing");

        var latestAdvisorySync = await dbContext.SyncJobLogs
            .Where(log => log.JobName == "SecurityAdvisorySync")
            .OrderByDescending(log => log.StartTime)
            .FirstOrDefaultAsync();

        var latestReply = await dbContext.PushLogs
            .OrderByDescending(log => log.CreatedTime)
            .FirstOrDefaultAsync();

        EnvironmentName = Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT") ?? "Production";
        InstanceName = appRuntimeOptions.Value.InstanceName;
        BotEnabled = telegramBotOptions.Value.Enabled;
        HasBotToken = !string.IsNullOrWhiteSpace(telegramBotOptions.Value.BotToken);
        AiProviderText = aiProviderOptions.Value.Provider;

        if (latestAdvisorySync is not null)
        {
            LatestAdvisorySyncText = $"{latestAdvisorySync.StartTime.ToOffset(TimeSpan.FromHours(8)):yyyy/MM/dd HH:mm} | {latestAdvisorySync.Message}";
        }

        RagIndexText = $"{RagChunkCount} chunks indexed for RAG retrieval.";

        if (latestReply is not null)
        {
            LatestReplyText = $"{latestReply.CreatedTime.ToOffset(TimeSpan.FromHours(8)):yyyy/MM/dd HH:mm} | {latestReply.MessageTitle} | {(latestReply.IsSuccess ? "success" : latestReply.ErrorMessage)}";
        }
    }
}
