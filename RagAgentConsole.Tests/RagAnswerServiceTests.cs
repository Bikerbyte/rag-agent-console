using RagAgentConsole.Models;
using RagAgentConsole.Services;
using Xunit;

namespace RagAgentConsole.Tests;

public class RagAnswerServiceTests
{
    [Fact]
    public async Task BuildAnswerWithTraceAsync_ReturnsCapabilitiesForEmptyMessage()
    {
        var retrieval = new RecordingRetrievalService();
        var result = await CreateService(retrieval).BuildAnswerWithTraceAsync(" \r\n ");

        Assert.Contains("知識庫", result.Content);
        Assert.Null(retrieval.LastQuestion);
    }

    [Fact]
    public async Task BuildAnswerWithTraceAsync_NormalizesWhitespaceBeforeRetrieval()
    {
        var retrieval = new RecordingRetrievalService();

        await CreateService(retrieval).BuildAnswerWithTraceAsync("備份\r\n需要\t什麼加密？");

        Assert.Equal("備份 需要 什麼加密？", retrieval.LastQuestion);
    }

    [Fact]
    public async Task BuildAnswerWithTraceAsync_PassesHistoryToRetrieval()
    {
        var retrieval = new RecordingRetrievalService();
        var history = new[]
        {
            new AgentConversationMessage("user", "備份政策是什麼？"),
            new AgentConversationMessage("assistant", "answer")
        };

        await CreateService(retrieval).BuildAnswerWithTraceAsync("那加密方式呢？", history);

        Assert.Same(history, retrieval.LastHistory);
    }

    private static RagAnswerService CreateService(IRagRetrievalService retrieval)
        => new(retrieval, new StubAppSettingsService(), new StubAiChatClient());

    private sealed class RecordingRetrievalService : IRagRetrievalService
    {
        public string? LastQuestion { get; private set; }
        public IReadOnlyList<AgentConversationMessage>? LastHistory { get; private set; }

        public Task<IReadOnlyList<RetrievalResult>> SearchAsync(string question, int maxResults = 5, CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<RetrievalResult>>([]);
        public Task<IReadOnlyList<RetrievalResult>> SearchAsync(string question, IReadOnlyList<AgentConversationMessage>? history, int maxResults = 5, CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<RetrievalResult>>([]);

        public Task<RetrievalResponse> SearchWithTraceAsync(
            string question,
            IReadOnlyList<AgentConversationMessage>? history = null,
            int maxResults = 5,
            string? moduleName = null,
            string retrievalMode = RetrievalModes.Hybrid,
            CancellationToken cancellationToken = default)
        {
            LastQuestion = question;
            LastHistory = history;
            var plan = new RetrievalPlan(question, question, null, [], [], RetrievalPlan.EmptyValues, RetrievalPlan.EmptyValues);
            return Task.FromResult(new RetrievalResponse(plan, retrievalMode, []));
        }
    }

    private sealed class StubAppSettingsService : IAppSettingsService
    {
        public Task<AiProviderOptions> GetAiProviderOptionsAsync(CancellationToken cancellationToken = default)
            => Task.FromResult(new AiProviderOptions());
        public Task<TelegramBotOptions> GetTelegramBotOptionsAsync(CancellationToken cancellationToken = default)
            => Task.FromResult(new TelegramBotOptions());
        public Task<RagOptions> GetRagOptionsAsync(CancellationToken cancellationToken = default)
            => Task.FromResult(new RagOptions());
        public Task<VectorStoreOptions> GetVectorStoreOptionsAsync(CancellationToken cancellationToken = default)
            => Task.FromResult(new VectorStoreOptions());
        public Task<ObservabilityOptions> GetObservabilityOptionsAsync(CancellationToken cancellationToken = default)
            => Task.FromResult(new ObservabilityOptions());
        public Task<AgentOptions> GetAgentOptionsAsync(CancellationToken cancellationToken = default)
            => Task.FromResult(new AgentOptions());
        public Task<IReadOnlyDictionary<string, AppSetting>> GetAllAsync(CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyDictionary<string, AppSetting>>(new Dictionary<string, AppSetting>());
        public Task SaveAsync(IEnumerable<AppSettingUpdate> updates, CancellationToken cancellationToken = default)
            => Task.CompletedTask;
    }

    private sealed class StubAiChatClient : IAiChatClient
    {
        public Task<string?> CompleteAsync(string systemPrompt, string userPrompt, CancellationToken cancellationToken = default)
            => Task.FromResult<string?>(null);
    }
}
