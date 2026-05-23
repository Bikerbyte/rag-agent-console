using SecurityAdvisoryBot.Models;

namespace SecurityAdvisoryBot.Services;

public interface IAdvisoryTextScorer
{
    double ScoreAdvisory(AdvisoryVectorSearchRequest request, SecurityAdvisory advisory, string chunkText);
    double ScoreDocument(AdvisoryVectorSearchRequest request, KnowledgeDocument document, string chunkText);
}

public interface IAiChatClient
{
    Task<string?> CompleteAsync(
        string systemPrompt,
        string userPrompt,
        CancellationToken cancellationToken = default);
}

public interface IAdvisoryEmbeddingService
{
    Task<float[]> BuildEmbeddingAsync(string text, CancellationToken cancellationToken = default);
}

public interface ISecurityAdvisoryAgentService
{
    Task<string> BuildReplyAsync(
        string messageText,
        string? chatId = null,
        IReadOnlyList<AdvisoryConversationMessage>? history = null,
        CancellationToken cancellationToken = default);

    Task<AgentAnswerResult> BuildReplyWithTraceAsync(
        string messageText,
        string? chatId = null,
        IReadOnlyList<AdvisoryConversationMessage>? history = null,
        CancellationToken cancellationToken = default);
}

public interface ISecurityAdvisoryAnswerService
{
    Task<string> BuildAnswerAsync(
        string question,
        IReadOnlyList<AdvisoryConversationMessage>? history = null,
        CancellationToken cancellationToken = default);

    Task<AgentAnswerResult> BuildAnswerWithTraceAsync(
        string question,
        IReadOnlyList<AdvisoryConversationMessage>? history = null,
        CancellationToken cancellationToken = default);
}

public interface ISecurityAdvisorySearchService
{
    Task<IReadOnlyList<SecurityAdvisorySearchResult>> SearchAsync(
        string question,
        int maxResults = 5,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<SecurityAdvisorySearchResult>> SearchAsync(
        string question,
        IReadOnlyList<AdvisoryConversationMessage>? history,
        int maxResults = 5,
        CancellationToken cancellationToken = default);

    Task<SecurityAdvisorySearchResponse> SearchWithTraceAsync(
        string question,
        IReadOnlyList<AdvisoryConversationMessage>? history = null,
        int maxResults = 5,
        string? moduleName = null,
        string retrievalMode = RetrievalModes.Hybrid,
        CancellationToken cancellationToken = default);
}

public interface IAdvisoryQueryPlanner
{
    Task<AdvisoryQueryPlan> BuildPlanAsync(
        string question,
        IReadOnlyList<AdvisoryConversationMessage>? history = null,
        CancellationToken cancellationToken = default);
}

public sealed record AdvisoryConversationMessage(string Role, string Content);
