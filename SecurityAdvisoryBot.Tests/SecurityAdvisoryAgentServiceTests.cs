using SecurityAdvisoryBot.Models;
using SecurityAdvisoryBot.Services;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace SecurityAdvisoryBot.Tests;

public class SecurityAdvisoryAgentServiceTests
{
    [Fact]
    public async Task BuildReplyAsync_DelegatesNaturalLanguageMessageToAnswerService()
    {
        var answerService = new FakeAnswerService();
        var service = new SecurityAdvisoryAgentService(
            answerService,
            NullLogger<SecurityAdvisoryAgentService>.Instance);

        var reply = await service.BuildReplyAsync("最近 Cisco 有哪些高風險 CVE？", "chat-1");

        Assert.Equal("answer: 最近 Cisco 有哪些高風險 CVE？", reply);
        Assert.Equal("最近 Cisco 有哪些高風險 CVE？", answerService.LastQuestion);
    }

    [Fact]
    public async Task BuildReplyAsync_UsesConversationHistory()
    {
        var answerService = new FakeAnswerService();
        var history = new[]
        {
            new AdvisoryConversationMessage("user", "最近 Cisco 有哪些高風險 CVE？"),
            new AdvisoryConversationMessage("assistant", "answer")
        };

        var service = new SecurityAdvisoryAgentService(
            answerService,
            NullLogger<SecurityAdvisoryAgentService>.Instance);

        await service.BuildReplyAsync("那哪些已經被利用？", "chat-1", history);

        Assert.Same(history, answerService.LastHistory);
    }

    [Fact]
    public async Task BuildReplyAsync_ReturnsCapabilitiesForEmptyMessage()
    {
        var service = new SecurityAdvisoryAgentService(
            new FakeAnswerService(),
            NullLogger<SecurityAdvisoryAgentService>.Instance);

        var reply = await service.BuildReplyAsync(" ");

        Assert.Contains("最近 Cisco 有哪些高風險 CVE", reply);
    }

    private sealed class FakeAnswerService : ISecurityAdvisoryAnswerService
    {
        public string? LastQuestion { get; private set; }
        public IReadOnlyList<AdvisoryConversationMessage>? LastHistory { get; private set; }

        public Task<string> BuildAnswerAsync(
            string question,
            IReadOnlyList<AdvisoryConversationMessage>? history = null,
            CancellationToken cancellationToken = default)
            => BuildAnswerWithTraceAsync(question, history, cancellationToken)
                .ContinueWith(task => task.Result.Content, cancellationToken);

        public Task<AgentAnswerResult> BuildAnswerWithTraceAsync(
            string question,
            IReadOnlyList<AdvisoryConversationMessage>? history = null,
            CancellationToken cancellationToken = default)
        {
            LastQuestion = question;
            LastHistory = history;
            return Task.FromResult(new AgentAnswerResult($"answer: {question}", null));
        }
    }
}
