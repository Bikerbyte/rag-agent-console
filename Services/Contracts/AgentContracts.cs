using RagAgentConsole.Models;

namespace RagAgentConsole.Services;

public interface IRetrievalTextScorer
{
    double ScoreAdvisory(RetrievalRequest request, SecurityAdvisory advisory, string chunkText);
    double ScoreDocument(RetrievalRequest request, KnowledgeDocument document, string chunkText);
}

public interface IAiChatClient
{
    Task<string?> CompleteAsync(
        string systemPrompt,
        string userPrompt,
        CancellationToken cancellationToken = default);
}

public interface IRagEmbeddingService
{
    Task<float[]> BuildEmbeddingAsync(string text, CancellationToken cancellationToken = default);
}

public interface IRagAnswerService
{
    Task<string> BuildAnswerAsync(
        string question,
        IReadOnlyList<AgentConversationMessage>? history = null,
        CancellationToken cancellationToken = default);

    Task<AgentAnswerResult> BuildAnswerWithTraceAsync(
        string question,
        IReadOnlyList<AgentConversationMessage>? history = null,
        CancellationToken cancellationToken = default);
}

public interface IRagRetrievalService
{
    Task<IReadOnlyList<RetrievalResult>> SearchAsync(
        string question,
        int maxResults = 5,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<RetrievalResult>> SearchAsync(
        string question,
        IReadOnlyList<AgentConversationMessage>? history,
        int maxResults = 5,
        CancellationToken cancellationToken = default);

    Task<RetrievalResponse> SearchWithTraceAsync(
        string question,
        IReadOnlyList<AgentConversationMessage>? history = null,
        int maxResults = 5,
        string? moduleName = null,
        string retrievalMode = RetrievalModes.Hybrid,
        CancellationToken cancellationToken = default);
}

public interface IRagQueryPlanner
{
    Task<RetrievalPlan> BuildPlanAsync(
        string question,
        IReadOnlyList<AgentConversationMessage>? history = null,
        CancellationToken cancellationToken = default);
}

public sealed record AgentConversationMessage(string Role, string Content);
