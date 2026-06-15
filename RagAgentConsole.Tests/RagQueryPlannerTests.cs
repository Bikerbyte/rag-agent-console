using RagAgentConsole.Models;
using RagAgentConsole.Services;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace RagAgentConsole.Tests;

public class RagQueryPlannerTests
{
    [Fact]
    public async Task BuildPlanAsync_WhenAiDisabled_FallsBackToRawQuestion()
    {
        var planner = CreatePlanner(enableChat: false, aiResponse: null);

        var plan = await planner.BuildPlanAsync("遠端工作核准流程");

        Assert.Equal(PlannerStrategy.RawFallback, plan.Strategy);
        Assert.Equal("遠端工作核准流程", plan.RetrievalQuery);
        Assert.Equal(KnowledgeModuleNames.InternalDocs, plan.ModuleName);
        Assert.NotEmpty(plan.SearchKeywords);
    }

    [Fact]
    public async Task BuildPlanAsync_WhenAiDisabled_UsesConfiguredDefaultModule()
    {
        var planner = CreatePlanner(
            enableChat: false,
            aiResponse: null,
            defaultModule: KnowledgeModuleNames.WorkflowQa);

        var plan = await planner.BuildPlanAsync("如何執行復原流程");

        Assert.Equal(KnowledgeModuleNames.WorkflowQa, plan.ModuleName);
    }

    [Fact]
    public async Task BuildPlanAsync_WhenAiReturnsValidJson_BuildsGenericPlan()
    {
        const string json = """
            {
              "intent": "policy_lookup",
              "moduleName": "InternalDocs",
              "retrievalQuery": "annual leave carryover policy",
              "searchKeywords": ["annual leave", "carryover"],
              "entities": { "region": "Taiwan" },
              "filters": { "department": "HR" },
              "notes": ["explicit department constraint"]
            }
            """;
        var planner = CreatePlanner(enableChat: true, aiResponse: json);

        var plan = await planner.BuildPlanAsync("HR 的特休可以遞延嗎");

        Assert.Equal(PlannerStrategy.Ai, plan.Strategy);
        Assert.Equal(KnowledgeModuleNames.InternalDocs, plan.ModuleName);
        Assert.Equal("annual leave carryover policy", plan.RetrievalQuery);
        Assert.Equal("Taiwan", plan.GetEntity("region"));
        Assert.Equal("HR", plan.GetFilter("department"));
    }

    [Fact]
    public async Task BuildPlanAsync_WhenAiReturnsJsonFence_StripsFence()
    {
        const string json = """
            ```json
            {
              "moduleName": "WorkflowQa",
              "retrievalQuery": "backup restoration runbook",
              "searchKeywords": ["backup", "restoration"],
              "entities": {},
              "filters": {},
              "notes": []
            }
            ```
            """;

        var plan = await CreatePlanner(true, json).BuildPlanAsync("備份怎麼復原");

        Assert.Equal(PlannerStrategy.Ai, plan.Strategy);
        Assert.Equal(KnowledgeModuleNames.WorkflowQa, plan.ModuleName);
    }

    [Fact]
    public async Task BuildPlanAsync_WhenAiReturnsUnknownModule_UsesConfiguredDefault()
    {
        const string json = """
            {
              "moduleName": "UnknownModule",
              "retrievalQuery": "test query",
              "searchKeywords": [],
              "entities": {},
              "filters": {},
              "notes": []
            }
            """;

        var plan = await CreatePlanner(true, json, KnowledgeModuleNames.WorkflowQa)
            .BuildPlanAsync("test");

        Assert.Equal(KnowledgeModuleNames.WorkflowQa, plan.ModuleName);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("not json")]
    public async Task BuildPlanAsync_WhenAiResponseCannotBeUsed_FallsBack(string? response)
    {
        var plan = await CreatePlanner(true, response).BuildPlanAsync("密碼政策");

        Assert.Equal(PlannerStrategy.RawFallback, plan.Strategy);
        Assert.Equal("密碼政策", plan.RetrievalQuery);
    }

    [Fact]
    public async Task BuildPlanAsync_WhenAiClientThrows_FallsBack()
    {
        var planner = new RagQueryPlanner(
            new ThrowingAiChatClient(),
            new FakeAppSettingsService(enableChat: true),
            new MixedScriptTokenizer(),
            NullLogger<RagQueryPlanner>.Instance);

        var plan = await planner.BuildPlanAsync("密碼政策");

        Assert.Equal(PlannerStrategy.RawFallback, plan.Strategy);
    }

    private static RagQueryPlanner CreatePlanner(
        bool enableChat,
        string? aiResponse,
        string defaultModule = KnowledgeModuleNames.InternalDocs)
        => new(
            new FakeAiChatClient(aiResponse),
            new FakeAppSettingsService(enableChat, defaultModule),
            new MixedScriptTokenizer(),
            NullLogger<RagQueryPlanner>.Instance);

    private sealed class FakeAiChatClient(string? response) : IAiChatClient
    {
        public Task<string?> CompleteAsync(string systemPrompt, string userPrompt, CancellationToken cancellationToken = default)
            => Task.FromResult(response);
    }

    private sealed class ThrowingAiChatClient : IAiChatClient
    {
        public Task<string?> CompleteAsync(string systemPrompt, string userPrompt, CancellationToken cancellationToken = default)
            => throw new HttpRequestException("provider unreachable");
    }

    private sealed class FakeAppSettingsService(bool enableChat, string defaultModule = KnowledgeModuleNames.InternalDocs)
        : IAppSettingsService
    {
        public Task<AiProviderOptions> GetAiProviderOptionsAsync(CancellationToken cancellationToken = default)
            => Task.FromResult(new AiProviderOptions
            {
                Provider = enableChat ? AiProviderNames.OpenAI : AiProviderNames.Local,
                EnableChatGeneration = enableChat
            });

        public Task<TelegramBotOptions> GetTelegramBotOptionsAsync(CancellationToken cancellationToken = default)
            => Task.FromResult(new TelegramBotOptions());
        public Task<RagOptions> GetRagOptionsAsync(CancellationToken cancellationToken = default)
            => Task.FromResult(new RagOptions());
        public Task<VectorStoreOptions> GetVectorStoreOptionsAsync(CancellationToken cancellationToken = default)
            => Task.FromResult(new VectorStoreOptions());
        public Task<ObservabilityOptions> GetObservabilityOptionsAsync(CancellationToken cancellationToken = default)
            => Task.FromResult(new ObservabilityOptions());
        public Task<AgentOptions> GetAgentOptionsAsync(CancellationToken cancellationToken = default)
            => Task.FromResult(new AgentOptions { DefaultModule = defaultModule });
        public Task<IReadOnlyDictionary<string, AppSetting>> GetAllAsync(CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyDictionary<string, AppSetting>>(new Dictionary<string, AppSetting>());
        public Task SaveAsync(IEnumerable<AppSettingUpdate> updates, CancellationToken cancellationToken = default)
            => Task.CompletedTask;
    }
}
