using System.Text;
using CPBLLineBotCloud.Data;
using CPBLLineBotCloud.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace CPBLLineBotCloud.Services;

public class SecurityAdvisoryNotificationDispatchService(
    ApplicationDbContext dbContext,
    ITelegramPushService telegramPushService,
    IOptions<PushNotificationOptions> pushOptions,
    IOptions<AppRuntimeOptions> runtimeOptions,
    TimeProvider timeProvider,
    ILogger<SecurityAdvisoryNotificationDispatchService> logger) : ITelegramNotificationDispatchService
{
    public async Task ProcessPendingNotificationsAsync(CancellationToken cancellationToken = default)
    {
        var options = pushOptions.Value;
        if (!options.Enabled || !options.EnableSecurityAdvisoryPush)
        {
            logger.LogDebug("Security advisory push notifications are disabled.");
            return;
        }

        var subscriptions = await dbContext.TelegramChatSubscriptions
            .Where(chat => chat.EnableAdvisoryPush)
            .OrderBy(chat => chat.ChatTitle)
            .ToListAsync(cancellationToken);

        if (subscriptions.Count == 0)
        {
            logger.LogDebug("No Telegram chats are subscribed to security advisory pushes.");
            return;
        }

        var cutoff = timeProvider.GetUtcNow().AddHours(-Math.Max(1, options.AdvisoryLookbackHours));
        var advisories = await dbContext.SecurityAdvisories
            .Where(advisory =>
                !advisory.IsSent &&
                advisory.LastSyncedTime >= cutoff &&
                (advisory.IsKnownExploited || advisory.CvssScore >= 9))
            .OrderByDescending(advisory => advisory.IsKnownExploited)
            .ThenByDescending(advisory => advisory.CvssScore)
            .ThenByDescending(advisory => advisory.LastSyncedTime)
            .Take(30)
            .ToListAsync(cancellationToken);

        foreach (var advisory in advisories)
        {
            var eligibleSubscriptions = subscriptions
                .Where(subscription => IsMatch(subscription, advisory))
                .ToList();

            if (eligibleSubscriptions.Count == 0)
            {
                advisory.IsSent = true;
                continue;
            }

            var title = BuildPushTitle(advisory);
            var body = BuildPushBody(advisory);
            var allDelivered = true;

            foreach (var subscription in eligibleSubscriptions)
            {
                var alreadySent = await HasSuccessfulPushAsync(subscription.ChatId, "SecurityAdvisory", title, cancellationToken);
                if (alreadySent)
                {
                    continue;
                }

                var isSuccess = await telegramPushService.SendPushAsync(subscription.ChatId, title, body, "SecurityAdvisory", cancellationToken);
                allDelivered &= isSuccess;
            }

            if (allDelivered)
            {
                advisory.IsSent = true;
            }
        }

        await dbContext.SaveChangesAsync(cancellationToken);
    }

    private async Task<bool> HasSuccessfulPushAsync(
        string chatId,
        string pushType,
        string title,
        CancellationToken cancellationToken)
    {
        return await dbContext.PushLogs.AnyAsync(log =>
            log.InstanceName == runtimeOptions.Value.InstanceName &&
            log.TargetGroupId == chatId &&
            log.PushType == pushType &&
            log.MessageTitle == title &&
            log.IsSuccess,
            cancellationToken);
    }

    private static bool IsMatch(TelegramChatSubscription subscription, SecurityAdvisory advisory)
    {
        var keywords = ParseKeywords(subscription.AdvisoryKeywords);
        if (keywords.Count == 0)
        {
            return advisory.IsKnownExploited || advisory.CvssScore >= 9;
        }

        var haystack = string.Join(' ', new[]
        {
            advisory.CveId,
            advisory.Title,
            advisory.Description,
            advisory.Vendor,
            advisory.Product,
            advisory.Tags
        }).ToLowerInvariant();

        return keywords.Any(keyword => haystack.Contains(keyword, StringComparison.OrdinalIgnoreCase));
    }

    private static IReadOnlyList<string> ParseKeywords(string? value)
        => string.IsNullOrWhiteSpace(value)
            ? []
            : value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Select(item => item.ToLowerInvariant())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

    private static string BuildPushTitle(SecurityAdvisory advisory)
    {
        var prefix = advisory.IsKnownExploited ? "KEV" : advisory.Severity ?? "Advisory";
        var id = advisory.CveId ?? advisory.ExternalId;
        return $"[{prefix}] {id}";
    }

    private static string BuildPushBody(SecurityAdvisory advisory)
    {
        var builder = new StringBuilder();
        builder.AppendLine(advisory.Title);
        builder.AppendLine($"風險: {BuildRiskText(advisory)}");
        builder.AppendLine($"影響: {BuildAffectedProduct(advisory)}");
        builder.AppendLine();
        builder.AppendLine(advisory.AiSummary ?? advisory.Description);

        if (!string.IsNullOrWhiteSpace(advisory.SuggestedAction))
        {
            builder.AppendLine();
            builder.AppendLine($"建議: {advisory.SuggestedAction}");
        }

        builder.AppendLine();
        builder.AppendLine($"查詢: /explain {advisory.CveId ?? advisory.ExternalId}");
        builder.AppendLine($"來源: {advisory.SourceUrl}");
        return builder.ToString().TrimEnd();
    }

    private static string BuildRiskText(SecurityAdvisory advisory)
    {
        var parts = new List<string>();
        if (!string.IsNullOrWhiteSpace(advisory.Severity))
        {
            parts.Add(advisory.Severity);
        }

        if (advisory.CvssScore.HasValue)
        {
            parts.Add($"CVSS {advisory.CvssScore:0.0}");
        }

        if (advisory.IsKnownExploited)
        {
            parts.Add("known exploited");
        }

        return parts.Count == 0 ? "未標示" : string.Join(" / ", parts);
    }

    private static string BuildAffectedProduct(SecurityAdvisory advisory)
    {
        var values = new[] { advisory.Vendor, advisory.Product }.Where(value => !string.IsNullOrWhiteSpace(value));
        var text = string.Join(" / ", values);
        return string.IsNullOrWhiteSpace(text) ? "未標示" : text;
    }
}
