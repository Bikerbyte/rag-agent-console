using RagAgentConsole.Models;
using RagAgentConsole.Services;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace RagAgentConsole.Tests;

public class AdvisoryQueryPlannerTests
{
    [Fact]
    public async Task BuildPlanAsync_WhenAiDisabled_ThrowsAiUnavailable()
    {
        var planner = CreatePlanner(enableChat: false, aiResponse: null);

        await Assert.ThrowsAsync<AiUnavailableException>(
            () => planner.BuildPlanAsync("citrix netscaler 弱點"));
    }

    [Fact]
    public async Task BuildPlanAsync_WhenAiReturnsValidJson_BuildsPlan()
    {
        const string json = """
            {
              "intent": "knowledge_lookup",
              "moduleName": "CveAdvisory",
              "vendor": "Citrix",
              "product": "NetScaler",
              "version": null,
              "cveId": null,
              "riskFilter": "none",
              "retrievalQuery": "Citrix NetScaler vulnerabilities",
              "searchKeywords": ["citrix", "netscaler"],
              "notes": [],
              "publishedFrom": null,
              "publishedTo": null,
              "preferRecent": false,
              "cveYear": null
            }
            """;
        var planner = CreatePlanner(enableChat: true, aiResponse: json);

        var plan = await planner.BuildPlanAsync("citrix netscaler 弱點");

        Assert.Equal(PlannerStrategy.Ai, plan.Strategy);
        Assert.Equal("Citrix NetScaler vulnerabilities", plan.RetrievalQuery);
        Assert.Contains("citrix", plan.SearchKeywords);
    }

    [Fact]
    public async Task BuildPlanAsync_WhenAiReturnsTemporalFields_ParsesDates()
    {
        const string json = """
            {
              "intent": "knowledge_lookup",
              "moduleName": "CveAdvisory",
              "vendor": null,
              "product": "windows server",
              "version": null,
              "cveId": null,
              "riskFilter": "none",
              "retrievalQuery": "windows server vulnerabilities since 2020",
              "searchKeywords": ["windows", "server"],
              "notes": [],
              "publishedFrom": "2020-01-01T00:00:00+00:00",
              "publishedTo": null,
              "preferRecent": true,
              "cveYear": null
            }
            """;
        var planner = CreatePlanner(enableChat: true, aiResponse: json);

        var plan = await planner.BuildPlanAsync("有沒有 2020 年以後的 windows server 弱點");

        Assert.Equal(new DateTimeOffset(2020, 1, 1, 0, 0, 0, TimeSpan.Zero), plan.PublishedFrom);
        Assert.Null(plan.PublishedTo);
        Assert.Null(plan.CveYear);
        Assert.True(plan.PreferRecent);
    }

    [Fact]
    public async Task BuildPlanAsync_WhenAiReturnsJsonFenced_StripsFenceAndParses()
    {
        const string fenced = """
            ```json
            {
              "intent": "knowledge_lookup",
              "moduleName": "CveAdvisory",
              "vendor": null,
              "product": null,
              "version": null,
              "cveId": null,
              "riskFilter": "known_exploited",
              "retrievalQuery": "known exploited vulnerabilities",
              "searchKeywords": [],
              "notes": [],
              "publishedFrom": null,
              "publishedTo": null,
              "preferRecent": false,
              "cveYear": null
            }
            ```
            """;
        var planner = CreatePlanner(enableChat: true, aiResponse: fenced);

        var plan = await planner.BuildPlanAsync("最近有哪些被利用的弱點");

        Assert.Equal(PlannerStrategy.Ai, plan.Strategy);
        Assert.Equal("known_exploited", plan.RiskFilter);
    }

    [Fact]
    public async Task BuildPlanAsync_WhenAiReturnsInvalidJson_FallsBackToRawQuestion()
    {
        var planner = CreatePlanner(enableChat: true, aiResponse: "not json at all");

        var plan = await planner.BuildPlanAsync("citrix netscaler 弱點");

        Assert.Equal(PlannerStrategy.Ai, plan.Strategy);
        Assert.Equal("citrix netscaler 弱點", plan.RetrievalQuery);
    }

    [Fact]
    public async Task BuildPlanAsync_WhenAiReturnsNull_FallsBackToRawQuestion()
    {
        var planner = CreatePlanner(enableChat: true, aiResponse: null);

        var plan = await planner.BuildPlanAsync("citrix netscaler 弱點");

        Assert.Equal(PlannerStrategy.Ai, plan.Strategy);
        Assert.Equal("citrix netscaler 弱點", plan.RetrievalQuery);
    }

    [Fact]
    public async Task BuildPlanAsync_WhenAiReturnsModuleName_MapsCorrectly()
    {
        const string json = """
            {
              "intent": "knowledge_lookup",
              "moduleName": "WorkflowQa",
              "retrievalQuery": "VPN SOP handling flow",
              "searchKeywords": ["vpn", "sop"],
              "notes": []
            }
            """;
        var planner = CreatePlanner(enableChat: true, aiResponse: json);

        var plan = await planner.BuildPlanAsync("公司 VPN SOP 的處理流程是什麼");

        Assert.Equal(KnowledgeModuleNames.WorkflowQa, plan.ModuleName);
    }

    [Fact]
    public async Task BuildPlanAsync_WhenAiReturnsUnknownModule_DefaultsToCveAdvisory()
    {
        const string json = """
            {
              "intent": "knowledge_lookup",
              "moduleName": "SomethingElse",
              "retrievalQuery": "test query",
              "searchKeywords": [],
              "notes": []
            }
            """;
        var planner = CreatePlanner(enableChat: true, aiResponse: json);

        var plan = await planner.BuildPlanAsync("隨便問一個");

        Assert.Equal(KnowledgeModuleNames.CveAdvisory, plan.ModuleName);
    }

    private static AdvisoryQueryPlanner CreatePlanner(bool enableChat, string? aiResponse)
    {
        return new AdvisoryQueryPlanner(
            new FakeAiChatClient(aiResponse),
            new FakeAppSettingsService(enableChat),
            NullLogger<AdvisoryQueryPlanner>.Instance);
    }

    private sealed class FakeAiChatClient(string? response) : IAiChatClient
    {
        public Task<string?> CompleteAsync(string systemPrompt, string userPrompt, CancellationToken cancellationToken = default)
            => Task.FromResult(response);
    }

    private sealed class FakeAppSettingsService(bool enableChat) : IAppSettingsService
    {
        public Task<AiProviderOptions> GetAiProviderOptionsAsync(CancellationToken cancellationToken = default)
            => Task.FromResult(new AiProviderOptions
            {
                Provider = enableChat ? AiProviderNames.OpenAI : AiProviderNames.Local,
                EnableChatGeneration = enableChat
            });
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
}
