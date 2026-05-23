using System.ComponentModel.DataAnnotations;
using System.Text.Json;
using SecurityAdvisoryBot.Models;
using SecurityAdvisoryBot.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace SecurityAdvisoryBot.Pages.Chat;

public class IndexModel(
    IAppSettingsService appSettingsService,
    ISecurityAdvisoryAgentService advisoryAgent,
    ILogger<IndexModel> logger) : PageModel
{
    public bool IsAiChatEnabled { get; private set; }
    public string ChatPlaceholder { get; private set; } = "Ask a question...";
    public IReadOnlyList<AgentChatMessageViewModel> AgentMessages { get; private set; } = [];

    [BindProperty]
    public AgentInputModel AgentInput { get; set; } = new();

    [BindProperty]
    public string? AgentHistoryJson { get; set; }

    public async Task OnGetAsync(CancellationToken cancellationToken)
    {
        await LoadRuntimeStateAsync(cancellationToken);
    }

    public async Task<IActionResult> OnPostClearAgentAsync(CancellationToken cancellationToken)
    {
        await LoadRuntimeStateAsync(cancellationToken);
        AgentMessages = [];
        AgentHistoryJson = null;
        AgentInput = new AgentInputModel();
        return Page();
    }

    public async Task<IActionResult> OnPostAskAgentAsync(CancellationToken cancellationToken)
    {
        await LoadRuntimeStateAsync(cancellationToken);
        var messages = ReadAgentHistory().ToList();

        if (string.IsNullOrWhiteSpace(AgentInput.Message))
        {
            AgentMessages = messages;
            AgentHistoryJson = JsonSerializer.Serialize(AgentMessages);
            return Page();
        }

        await BuildAgentReplyAsync(messages, cancellationToken);
        AgentMessages = messages.TakeLast(16).ToList();
        AgentHistoryJson = JsonSerializer.Serialize(AgentMessages);
        AgentInput = new AgentInputModel();
        ModelState.Clear();
        return Page();
    }

    public async Task<IActionResult> OnPostAskAgentJsonAsync(CancellationToken cancellationToken)
    {
        var messages = ReadAgentHistory().ToList();

        if (string.IsNullOrWhiteSpace(AgentInput.Message))
        {
            return new JsonResult(new
            {
                historyJson = JsonSerializer.Serialize(messages),
                newMessages = Array.Empty<AgentChatMessageViewModel>()
            });
        }

        var agentChatMessage = await BuildAgentReplyAsync(messages, cancellationToken);
        var retainedMessages = messages.TakeLast(16).ToList();
        return new JsonResult(new
        {
            historyJson = JsonSerializer.Serialize(retainedMessages),
            newMessages = new[] { agentChatMessage }
        });
    }

    private async Task<AgentChatMessageViewModel> BuildAgentReplyAsync(
        List<AgentChatMessageViewModel> messages,
        CancellationToken cancellationToken)
    {
        var history = messages
            .Select(message => new AdvisoryConversationMessage(message.Role, message.Content))
            .ToList();

        var userMessage = AgentInput.Message!.Trim();
        messages.Add(new AgentChatMessageViewModel("user", userMessage));

        var agentReply = await advisoryAgent.BuildReplyWithTraceAsync(userMessage, "web-chat", history, cancellationToken);
        var agentChatMessage = new AgentChatMessageViewModel("assistant", agentReply.Content, AgentTraceViewModel.FromTrace(agentReply.Trace));
        messages.Add(agentChatMessage);
        return agentChatMessage;
    }

    private async Task LoadRuntimeStateAsync(CancellationToken cancellationToken)
    {
        var currentAiOptions = await appSettingsService.GetAiProviderOptionsAsync(cancellationToken);
        IsAiChatEnabled = currentAiOptions.EnableChatGeneration &&
            !string.Equals(currentAiOptions.Provider, AiProviderNames.Local, StringComparison.OrdinalIgnoreCase);
        var agentOptions = await appSettingsService.GetAgentOptionsAsync(cancellationToken);
        ChatPlaceholder = agentOptions.ChatPlaceholder;
    }

    private IReadOnlyList<AgentChatMessageViewModel> ReadAgentHistory()
    {
        if (string.IsNullOrWhiteSpace(AgentHistoryJson))
        {
            return [];
        }

        try
        {
            return JsonSerializer.Deserialize<List<AgentChatMessageViewModel>>(AgentHistoryJson) ?? [];
        }
        catch (JsonException exception)
        {
            logger.LogWarning(exception, "Failed to parse web chat history.");
            return [];
        }
    }

    public class AgentInputModel
    {
        [StringLength(800)]
        public string? Message { get; set; }
    }

    public sealed record AgentChatMessageViewModel(
        string Role,
        string Content,
        AgentTraceViewModel? Trace = null);

    public sealed record AgentTraceViewModel(
        string Intent,
        string ModuleName,
        string RetrievalQuery,
        string RetrievalMode,
        string? RiskFilter,
        string? Version,
        IReadOnlyList<AgentTraceMatchViewModel> Matches)
    {
        public static AgentTraceViewModel? FromTrace(AgentRetrievalTrace? trace)
        {
            if (trace is null)
            {
                return null;
            }

            return new AgentTraceViewModel(
                trace.Planner.Intent ?? "-",
                trace.Planner.ModuleName,
                trace.Planner.RetrievalQuery,
                trace.RetrievalMode,
                trace.Planner.RiskFilter,
                trace.Planner.Version,
                trace.Matches.Select(match => new AgentTraceMatchViewModel(
                    match.Rank,
                    match.ModuleName,
                    match.SourceKind,
                    match.CveId ?? match.Title,
                    match.Score,
                    match.VectorScore,
                    match.TextScore)).ToList());
        }
    }

    public sealed record AgentTraceMatchViewModel(
        int Rank,
        string ModuleName,
        string SourceKind,
        string Label,
        double Score,
        double VectorScore,
        double TextScore);
}
