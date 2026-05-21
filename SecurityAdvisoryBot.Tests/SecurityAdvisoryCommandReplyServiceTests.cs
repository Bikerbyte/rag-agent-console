using SecurityAdvisoryBot.Data;
using SecurityAdvisoryBot.Models;
using SecurityAdvisoryBot.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace SecurityAdvisoryBot.Tests;

public class SecurityAdvisoryCommandReplyServiceTests
{
    [Fact]
    public async Task ExplainReply_ReturnsStoredAdvisoryDetails()
    {
        await using var dbContext = CreateDbContext();
        dbContext.SecurityAdvisories.Add(new SecurityAdvisory
        {
            SourceName = "CISA KEV",
            ExternalId = "CVE-2024-3094",
            CveId = "CVE-2024-3094",
            Title = "XZ Utils Backdoor",
            Description = "Malicious code was discovered in XZ Utils.",
            Vendor = "Tukaani",
            Product = "XZ Utils",
            Severity = "Critical",
            CvssScore = 10,
            IsKnownExploited = true,
            SourceUrl = "https://nvd.nist.gov/vuln/detail/CVE-2024-3094",
            ContentHash = "hash",
            AiSummary = "XZ Utils includes a backdoor risk.",
            SuggestedAction = "Patch immediately.",
            CreatedTime = DateTimeOffset.UtcNow,
            LastSyncedTime = DateTimeOffset.UtcNow
        });
        await dbContext.SaveChangesAsync();

        var service = CreateService(dbContext);
        var reply = await service.BuildReplyAsync("/explain CVE-2024-3094");

        Assert.Contains("CVE-2024-3094", reply);
        Assert.Contains("known exploited", reply);
        Assert.Contains("Patch immediately.", reply);
    }

    [Fact]
    public async Task SubscribeReply_UpdatesChatAdvisoryKeywords()
    {
        await using var dbContext = CreateDbContext();
        dbContext.TelegramChatSubscriptions.Add(new TelegramChatSubscription
        {
            ChatId = "chat-1",
            ChatTitle = "Security Team",
            EnableAdvisoryPush = false,
            CreatedTime = DateTimeOffset.UtcNow,
            LastUpdatedTime = DateTimeOffset.UtcNow
        });
        await dbContext.SaveChangesAsync();

        var service = CreateService(dbContext);
        var reply = await service.BuildReplyAsync("/subscribe fortinet azure", "chat-1");
        var subscription = await dbContext.TelegramChatSubscriptions.SingleAsync();

        Assert.Contains("已更新弱點訂閱", reply);
        Assert.True(subscription.EnableAdvisoryPush);
        Assert.Contains("fortinet", subscription.AdvisoryKeywords);
        Assert.Contains("azure", subscription.AdvisoryKeywords);
    }

    private static ApplicationDbContext CreateDbContext()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        return new ApplicationDbContext(options);
    }

    private static SecurityAdvisoryCommandReplyService CreateService(ApplicationDbContext dbContext)
    {
        return new SecurityAdvisoryCommandReplyService(
            dbContext,
            new FakeSyncService(),
            new FakeAnswerService(),
            NullLogger<SecurityAdvisoryCommandReplyService>.Instance);
    }

    private sealed class FakeSyncService : ISecurityAdvisorySyncService
    {
        public Task<SecurityAdvisorySyncResult> SyncAsync(CancellationToken cancellationToken = default)
            => Task.FromResult(new SecurityAdvisorySyncResult(0, 0, 0, 0, 0));
    }

    private sealed class FakeAnswerService : ISecurityAdvisoryAnswerService
    {
        public Task<string> BuildAnswerAsync(string question, CancellationToken cancellationToken = default)
            => Task.FromResult($"answer: {question}");
    }
}
