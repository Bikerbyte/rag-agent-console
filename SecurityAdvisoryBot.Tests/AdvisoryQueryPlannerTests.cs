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

    private static AdvisoryQueryPlanner CreatePlanner()
        => new(
            new FakeAiChatClient(),
            Options.Create(new AiProviderOptions
            {
                Provider = AiProviderNames.Local,
                EnableChatGeneration = false,
                UseLocalFallback = true
            }),
            NullLogger<AdvisoryQueryPlanner>.Instance);

    private sealed class FakeAiChatClient : IAiChatClient
    {
        public Task<string?> CompleteAsync(
            string systemPrompt,
            string userPrompt,
            CancellationToken cancellationToken = default)
            => Task.FromResult<string?>(null);
    }
}
