using RagAgentConsole.Models;
using RagAgentConsole.Services;
using Microsoft.Extensions.Options;
using Xunit;

namespace RagAgentConsole.Tests;

public class RagAnswerServiceTests
{
    [Fact]
    public async Task BuildAnswerWithTraceAsync_ReturnsCapabilitiesForEmptyMessage()
    {
        var retrieval = new RecordingRetrievalService();
        var service = CreateService(retrieval);

        var result = await service.BuildAnswerWithTraceAsync(" \r\n ");

        Assert.Contains("知識庫", result.Content);
        Assert.Null(retrieval.LastQuestion);
    }

    [Fact]
    public async Task BuildAnswerWithTraceAsync_NormalizesWhitespaceBeforeRetrieval()
    {
        var retrieval = new RecordingRetrievalService();
        var service = CreateService(retrieval);

        await service.BuildAnswerWithTraceAsync("最近\r\n有哪些\t高風險 CVE？");

        Assert.Equal("最近 有哪些 高風險 CVE？", retrieval.LastQuestion);
    }

    [Fact]
    public async Task BuildAnswerWithTraceAsync_PassesHistoryToRetrieval()
    {
        var retrieval = new RecordingRetrievalService();
        var service = CreateService(retrieval);
        var history = new[]
        {
            new AgentConversationMessage("user", "最近 Cisco 有哪些高風險 CVE？"),
            new AgentConversationMessage("assistant", "answer")
        };

        await service.BuildAnswerWithTraceAsync("那哪些已經被利用？", history);

        Assert.Same(history, retrieval.LastHistory);
    }

    private static RagAnswerService CreateService(IRagRetrievalService retrieval)
        => new(
            retrieval,
            new StubAppSettingsService(),
            new StubDomainRegistry(),
            Options.Create(new SecurityAdvisoryOptions()),
            Options.Create(new AiProviderOptions()),
            new StubAiChatClient());

    private sealed class RecordingRetrievalService : IRagRetrievalService
    {
        public string? LastQuestion { get; private set; }
        public IReadOnlyList<AgentConversationMessage>? LastHistory { get; private set; }

        public Task<IReadOnlyList<RetrievalResult>> SearchAsync(
            string question,
            int maxResults = 5,
            CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<RetrievalResult>>([]);

        public Task<IReadOnlyList<RetrievalResult>> SearchAsync(
            string question,
            IReadOnlyList<AgentConversationMessage>? history,
            int maxResults = 5,
            CancellationToken cancellationToken = default)
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
            var plan = new RetrievalPlan(
                question,
                question,
                null,
                [],
                [],
                RetrievalPlan.EmptyValues,
                RetrievalPlan.EmptyValues);
            return Task.FromResult(new RetrievalResponse(plan, retrievalMode, []));
        }
    }

    private sealed class StubAppSettingsService : IAppSettingsService
    {
        public Task<AiProviderOptions> GetAiProviderOptionsAsync(CancellationToken cancellationToken = default)
            => Task.FromResult(new AiProviderOptions());

        public Task<TelegramBotOptions> GetTelegramBotOptionsAsync(CancellationToken cancellationToken = default)
            => Task.FromResult(new TelegramBotOptions());

        public Task<DataSourceOptions> GetDataSourceOptionsAsync(CancellationToken cancellationToken = default)
            => Task.FromResult(new DataSourceOptions());

        public Task<PushNotificationOptions> GetPushNotificationOptionsAsync(CancellationToken cancellationToken = default)
            => Task.FromResult(new PushNotificationOptions());

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

    private sealed class StubDomainRegistry : IRagDomainRegistry
    {
        public IRagDomain DefaultDomain
            => throw new NotSupportedException("Not expected in these tests.");

        public IRagDomain Resolve(string? moduleName)
            => throw new NotSupportedException("Not expected in these tests.");

        public IRagDomain? FindByName(string? domainName)
            => throw new NotSupportedException("Not expected in these tests.");

        public IRagDomain ResolveForResult(RetrievalResult result)
            => throw new NotSupportedException("Not expected in these tests.");

        public string NormalizeModuleName(string? moduleName)
            => throw new NotSupportedException("Not expected in these tests.");

        public string? TryNormalizeModuleName(string? moduleName)
            => throw new NotSupportedException("Not expected in these tests.");

        public IReadOnlyList<IRagDomain> ListDomains()
            => throw new NotSupportedException("Not expected in these tests.");
    }

    private sealed class StubAiChatClient : IAiChatClient
    {
        public Task<string?> CompleteAsync(
            string systemPrompt,
            string userPrompt,
            CancellationToken cancellationToken = default)
            => Task.FromResult<string?>(null);
    }
}
