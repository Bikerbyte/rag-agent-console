using SecurityAdvisoryBot.Models;
using SecurityAdvisoryBot.Services;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace SecurityAdvisoryBot.Tests;

public class AdvisoryQueryPlannerTests
{
    [Fact]
    public async Task BuildPlanAsync_WhenQuestionContainsVersion_KeepsVersionOutOfSearchKeywords()
    {
        var planner = CreatePlanner();

        var plan = await planner.BuildPlanAsync("citrix netscaler 59.22 版本有弱點嗎");

        Assert.Equal("59.22", plan.Version);
        Assert.Contains("citrix", plan.SearchKeywords);
        Assert.Contains("netscaler", plan.SearchKeywords);
        Assert.DoesNotContain("59.22", plan.SearchKeywords);
        Assert.Contains("citrix netscaler", plan.RetrievalQuery, StringComparison.OrdinalIgnoreCase);
        Assert.Contains(plan.Notes, note => note.Contains("version", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task BuildPlanAsync_WhenQuestionAsksKnownExploited_MapsRiskFilter()
    {
        var planner = CreatePlanner();

        var plan = await planner.BuildPlanAsync("Citrix NetScaler 有已知遭利用弱點嗎");

        Assert.Equal("known_exploited", plan.RiskFilter);
        Assert.True(plan.KevOnly);
        Assert.Contains("known exploited", plan.RetrievalQuery, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task BuildPlanAsync_WhenFollowUpContainsOnlyVersion_UsesPreviousUserContext()
    {
        var planner = CreatePlanner();
        var history = new[]
        {
            new AdvisoryConversationMessage("user", "citrix netscaler 59.22 版本有弱點嗎"),
            new AdvisoryConversationMessage("assistant", "目前資料不足以確認該版本，但 Citrix NetScaler 有相關 CVE。")
        };

        var plan = await planner.BuildPlanAsync("2402 呢?", history);

        Assert.Equal("2402", plan.Version);
        Assert.Contains("citrix", plan.SearchKeywords);
        Assert.Contains("netscaler", plan.SearchKeywords);
        Assert.DoesNotContain("2402", plan.SearchKeywords);
        Assert.Contains(plan.Notes, note => note.Contains("follow-up context", StringComparison.OrdinalIgnoreCase));
    }

    [Theory]
    [InlineData("公司 VPN SOP 的處理流程是什麼", KnowledgeModuleNames.WorkflowQa)]
    [InlineData("查一下內部政策文件怎麼寫", KnowledgeModuleNames.InternalDocs)]
    [InlineData("最近 Cisco 有哪些高風險 CVE", KnowledgeModuleNames.CveAdvisory)]
    public async Task BuildPlanAsync_SelectsKnowledgeModule(string question, string expectedModule)
    {
        var planner = CreatePlanner();

        var plan = await planner.BuildPlanAsync(question);

        Assert.Equal(expectedModule, plan.ModuleName);
    }

    [Fact]
    public async Task BuildPlanAsync_LocalHeuristic_SetsStrategyField()
    {
        var planner = CreatePlanner();

        var plan = await planner.BuildPlanAsync("最近 Cisco 有哪些高風險 CVE");

        Assert.Equal(PlannerStrategy.LocalHeuristic, plan.Strategy);
    }

    private static LocalAdvisoryQueryPlanner CreatePlanner()
        => new(NullLogger<LocalAdvisoryQueryPlanner>.Instance);
}

public class ResilientAdvisoryQueryPlannerTests
{
    [Fact]
    public async Task BuildPlanAsync_WhenAiDisabled_UsesLocalPlanner()
    {
        var planner = CreatePlanner(enableChat: false, aiResponse: null);

        var plan = await planner.BuildPlanAsync("citrix netscaler 弱點");

        Assert.Equal(PlannerStrategy.LocalHeuristic, plan.Strategy);
    }

    [Fact]
    public async Task BuildPlanAsync_WhenAiEnabledAndReturnsValidJson_UsesAiPlan()
    {
        const string json = """
            {
              "intent": "vulnerability_lookup",
              "moduleName": "CveAdvisory",
              "vendor": "Citrix",
              "product": "NetScaler",
              "version": null,
              "cveId": null,
              "riskFilter": "none",
              "retrievalQuery": "Citrix NetScaler vulnerabilities",
              "searchKeywords": ["citrix", "netscaler"],
              "notes": []
            }
            """;
        var planner = CreatePlanner(enableChat: true, aiResponse: json);

        var plan = await planner.BuildPlanAsync("citrix netscaler 弱點");

        Assert.Equal(PlannerStrategy.Ai, plan.Strategy);
        Assert.Equal("Citrix NetScaler vulnerabilities", plan.RetrievalQuery);
        Assert.Contains("citrix", plan.SearchKeywords);
    }

    [Fact]
    public async Task BuildPlanAsync_WhenAiReturnsInvalidJson_FallsBackToLocal()
    {
        var planner = CreatePlanner(enableChat: true, aiResponse: "not json at all");

        var plan = await planner.BuildPlanAsync("citrix netscaler 弱點");

        Assert.Equal(PlannerStrategy.LocalHeuristic, plan.Strategy);
    }

    [Fact]
    public async Task BuildPlanAsync_WhenAiReturnsNull_FallsBackToLocal()
    {
        var planner = CreatePlanner(enableChat: true, aiResponse: null);

        var plan = await planner.BuildPlanAsync("citrix netscaler 弱點");

        Assert.Equal(PlannerStrategy.LocalHeuristic, plan.Strategy);
    }

    [Fact]
    public async Task BuildPlanAsync_WhenAiReturnsJsonFencedResponse_StripsFenceAndParses()
    {
        const string fenced = """
            ```json
            {
              "intent": "vulnerability_lookup",
              "moduleName": "CveAdvisory",
              "vendor": null,
              "product": null,
              "version": null,
              "cveId": null,
              "riskFilter": "known_exploited",
              "retrievalQuery": "known exploited vulnerabilities",
              "searchKeywords": [],
              "notes": []
            }
            ```
            """;
        var planner = CreatePlanner(enableChat: true, aiResponse: fenced);

        var plan = await planner.BuildPlanAsync("最近有哪些被利用的弱點");

        Assert.Equal(PlannerStrategy.Ai, plan.Strategy);
        Assert.Equal("known_exploited", plan.RiskFilter);
    }

    private static ResilientAdvisoryQueryPlanner CreatePlanner(bool enableChat, string? aiResponse)
    {
        var local = new LocalAdvisoryQueryPlanner(NullLogger<LocalAdvisoryQueryPlanner>.Instance);
        var aiOptions = Options.Create(new AiProviderOptions
        {
            Provider = enableChat ? AiProviderNames.OpenAI : AiProviderNames.Local,
            EnableChatGeneration = enableChat
        });
        return new ResilientAdvisoryQueryPlanner(
            new FakeAiChatClient(aiResponse),
            local,
            aiOptions,
            NullLogger<ResilientAdvisoryQueryPlanner>.Instance);
    }

    private sealed class FakeAiChatClient(string? response) : IAiChatClient
    {
        public Task<string?> CompleteAsync(string systemPrompt, string userPrompt, CancellationToken cancellationToken = default)
            => Task.FromResult(response);
    }
}
