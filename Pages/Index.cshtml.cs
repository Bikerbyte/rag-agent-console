using RagAgentConsole.Data;
using RagAgentConsole.Models;
using RagAgentConsole.Services;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace RagAgentConsole.Pages;

public class IndexModel(
    ApplicationDbContext dbContext,
    IOptions<AppRuntimeOptions> appRuntimeOptions,
    IAppSettingsService appSettingsService) : PageModel
{
    public string AgentName { get; private set; } = "RAG Agent Console";
    public string AgentTagline { get; private set; } = "Document knowledge agent";
    public int DocumentCount { get; private set; }
    public int RagChunkCount { get; private set; }
    public int ChatCount { get; private set; }
    public int PendingTelegramUpdateCount { get; private set; }
    public string EnvironmentName { get; private set; } = string.Empty;
    public string InstanceName { get; private set; } = string.Empty;
    public bool BotEnabled { get; private set; }
    public bool HasBotToken { get; private set; }
    public string AiProviderText { get; private set; } = string.Empty;
    public string LatestDocumentText { get; private set; } = "No knowledge document indexed yet.";
    public string RagIndexText { get; private set; } = "No RAG chunks indexed yet.";
    public string LatestReplyText { get; private set; } = "No Telegram delivery log yet.";

    public async Task OnGetAsync(CancellationToken cancellationToken)
    {
        var agentOptions = await appSettingsService.GetAgentOptionsAsync(cancellationToken);
        var aiProviderOptions = await appSettingsService.GetAiProviderOptionsAsync(cancellationToken);
        var telegramBotOptions = await appSettingsService.GetTelegramBotOptionsAsync(cancellationToken);
        AgentName = agentOptions.AgentName;
        AgentTagline = agentOptions.AgentTagline;

        DocumentCount = await dbContext.KnowledgeDocuments.CountAsync(cancellationToken);
        RagChunkCount = await dbContext.KnowledgeDocumentChunks.CountAsync(cancellationToken);
        ChatCount = await dbContext.TelegramChatSubscriptions.CountAsync(cancellationToken);
        PendingTelegramUpdateCount = await dbContext.TelegramUpdateInboxes
            .CountAsync(item => item.Status == "Pending" || item.Status == "Processing", cancellationToken);

        var latestDocument = await dbContext.KnowledgeDocuments
            .AsNoTracking()
            .OrderByDescending(document => document.LastUpdatedTime)
            .FirstOrDefaultAsync(cancellationToken);
        var latestReply = await dbContext.PushLogs
            .AsNoTracking()
            .OrderByDescending(log => log.CreatedTime)
            .FirstOrDefaultAsync(cancellationToken);

        EnvironmentName = Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT") ?? "Production";
        InstanceName = appRuntimeOptions.Value.InstanceName;
        BotEnabled = telegramBotOptions.Enabled;
        HasBotToken = !string.IsNullOrWhiteSpace(telegramBotOptions.BotToken);
        AiProviderText = aiProviderOptions.Provider;

        if (latestDocument is not null)
        {
            LatestDocumentText = $"{latestDocument.LastUpdatedTime.ToLocalTime():yyyy/MM/dd HH:mm} | {latestDocument.Title} | {latestDocument.ChunkCount} chunks";
        }

        RagIndexText = $"{RagChunkCount} chunks indexed across {DocumentCount} documents.";
        if (latestReply is not null)
        {
            LatestReplyText = $"{latestReply.CreatedTime.ToLocalTime():yyyy/MM/dd HH:mm} | {latestReply.MessageTitle} | {(latestReply.IsSuccess ? "success" : latestReply.ErrorMessage)}";
        }
    }
}
