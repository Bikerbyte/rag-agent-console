using RagAgentConsole.Models;
using RagAgentConsole.Services;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace RagAgentConsole.Tests;

public class RagAgentServiceTests
{
    [Fact]
    public async Task BuildReplyAsync_DelegatesNaturalLanguageMessageToAnswerService()
    {
        var answerService = new FakeAnswerService();
        var service = new RagAgentService(
            answerService,
            NullLogger<RagAgentService>.Instance);

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
            new AgentConversationMessage("user", "最近 Cisco 有哪些高風險 CVE？"),
            new AgentConversationMessage("assistant", "answer")
        };

        var service = new RagAgentService(
            answerService,
            NullLogger<RagAgentService>.Instance);

        await service.BuildReplyAsync("那哪些已經被利用？", "chat-1", history);

        Assert.Same(history, answerService.LastHistory);
    }

    [Fact]
    public async Task BuildReplyAsync_ReturnsCapabilitiesForEmptyMessage()
    {
        var service = new RagAgentService(
            new FakeAnswerService(),
            NullLogger<RagAgentService>.Instance);

        var reply = await service.BuildReplyAsync(" ");

        Assert.Contains("知識庫", reply);
    }

    private sealed class FakeAnswerService : IRagAnswerService
    {
        public string? LastQuestion { get; private set; }
        public IReadOnlyList<AgentConversationMessage>? LastHistory { get; private set; }

        public Task<string> BuildAnswerAsync(
            string question,
            IReadOnlyList<AgentConversationMessage>? history = null,
            CancellationToken cancellationToken = default)
            => BuildAnswerWithTraceAsync(question, history, cancellationToken)
                .ContinueWith(task => task.Result.Content, cancellationToken);

        public Task<AgentAnswerResult> BuildAnswerWithTraceAsync(
            string question,
            IReadOnlyList<AgentConversationMessage>? history = null,
            CancellationToken cancellationToken = default)
        {
            LastQuestion = question;
            LastHistory = history;
            return Task.FromResult(new AgentAnswerResult($"answer: {question}", null));
        }
    }
}
