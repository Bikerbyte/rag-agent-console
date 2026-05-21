using System.Text;
using System.Text.RegularExpressions;
using SecurityAdvisoryBot.Data;
using SecurityAdvisoryBot.Models;
using Microsoft.EntityFrameworkCore;

namespace SecurityAdvisoryBot.Services;

public partial class SecurityAdvisoryCommandReplyService(
    ApplicationDbContext dbContext,
    ISecurityAdvisorySyncService advisorySyncService,
    ISecurityAdvisoryAnswerService answerService,
    ILogger<SecurityAdvisoryCommandReplyService> logger) : ICommandReplyService
{
    public async Task<string> BuildReplyAsync(string commandText, string? chatId = null, CancellationToken cancellationToken = default)
    {
        var normalized = NormalizeCommand(commandText);
        if (string.IsNullOrWhiteSpace(normalized))
        {
            return BuildHelpReply();
        }

        logger.LogInformation("Building security advisory command reply for text: {CommandText}", normalized);
        var (command, argument) = SplitCommand(normalized);

        return command switch
        {
            "/help" or "help" or "說明" => BuildHelpReply(),
            "/sync" => await BuildSyncReplyAsync(cancellationToken),
            "/latest" or "最新" => await BuildLatestReplyAsync(argument, cancellationToken),
            "/critical" or "高風險" => await BuildCriticalReplyAsync(cancellationToken),
            "/kev" or "已被利用" => await BuildKevReplyAsync(cancellationToken),
            "/explain" or "解釋" => await BuildExplainReplyAsync(argument, cancellationToken),
            "/ask" or "問" => await BuildAskReplyAsync(argument, cancellationToken),
            "/subscribe" or "/watch" or "訂閱" => await BuildSubscribeReplyAsync(chatId, argument, cancellationToken),
            "/unsubscribe" or "/unwatch" or "取消訂閱" => await BuildUnsubscribeReplyAsync(chatId, argument, cancellationToken),
            "/watchlist" or "/following" or "訂閱清單" => await BuildWatchlistReplyAsync(chatId, cancellationToken),
            _ => await BuildSmartFallbackReplyAsync(normalized, cancellationToken)
        };
    }

    private async Task<string> BuildSyncReplyAsync(CancellationToken cancellationToken)
    {
        var result = await advisorySyncService.SyncAsync(cancellationToken);
        return $"弱點資料同步完成\n來源: {result.SourceCount}\n取得: {result.FetchedCount}\n新增: {result.AddedCount}\n更新: {result.UpdatedCount}\n索引 chunks: {result.ChunkCount}";
    }

    private async Task<string> BuildLatestReplyAsync(string? query, CancellationToken cancellationToken)
    {
        var advisories = dbContext.SecurityAdvisories.AsQueryable();
        if (!string.IsNullOrWhiteSpace(query))
        {
            advisories = ApplyKeywordFilter(advisories, query);
        }

        var items = await advisories
            .OrderByDescending(advisory => advisory.LastModifiedAt ?? advisory.PublishedAt ?? advisory.LastSyncedTime)
            .Take(6)
            .ToListAsync(cancellationToken);

        return BuildAdvisoryListReply(
            string.IsNullOrWhiteSpace(query) ? "最新弱點情報" : $"最新弱點情報 | {query}",
            items);
    }

    private async Task<string> BuildCriticalReplyAsync(CancellationToken cancellationToken)
    {
        var items = await dbContext.SecurityAdvisories
            .Where(advisory => advisory.CvssScore >= 9 || advisory.Severity == "CRITICAL" || advisory.Severity == "Critical")
            .OrderByDescending(advisory => advisory.LastModifiedAt ?? advisory.PublishedAt ?? advisory.LastSyncedTime)
            .Take(6)
            .ToListAsync(cancellationToken);

        return BuildAdvisoryListReply("Critical 弱點", items);
    }

    private async Task<string> BuildKevReplyAsync(CancellationToken cancellationToken)
    {
        var items = await dbContext.SecurityAdvisories
            .Where(advisory => advisory.IsKnownExploited)
            .OrderByDescending(advisory => advisory.PublishedAt ?? advisory.LastSyncedTime)
            .Take(6)
            .ToListAsync(cancellationToken);

        return BuildAdvisoryListReply("CISA KEV 已知遭利用弱點", items);
    }

    private async Task<string> BuildExplainReplyAsync(string? argument, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(argument))
        {
            return "請指定 CVE ID，例如：/explain CVE-2024-3094";
        }

        var cveId = ExtractCveId(argument) ?? argument.Trim();
        var advisory = await dbContext.SecurityAdvisories
            .Where(item => item.CveId == cveId || item.ExternalId == cveId)
            .OrderByDescending(item => item.IsKnownExploited)
            .ThenByDescending(item => item.CvssScore)
            .FirstOrDefaultAsync(cancellationToken);

        if (advisory is null)
        {
            return $"目前找不到 {cveId}。可以先執行 /sync，或用 /ask {cveId} 試著用 RAG 搜尋相關資料。";
        }

        return BuildAdvisoryDetailReply(advisory);
    }

    private async Task<string> BuildAskReplyAsync(string? argument, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(argument))
        {
            return "請在 /ask 後面接問題，例如：/ask 最近 Fortinet 有哪些已被利用的弱點？";
        }

        return await answerService.BuildAnswerAsync(argument, cancellationToken);
    }

    private async Task<string> BuildSubscribeReplyAsync(string? chatId, string? argument, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(chatId))
        {
            return "這個指令需要在 Telegram chat 裡使用，系統才知道要更新哪個訂閱設定。";
        }

        var keywords = ParseKeywords(argument);
        if (keywords.Count == 0)
        {
            return "請指定要訂閱的關鍵字，例如：/subscribe fortinet azure windows";
        }

        var subscription = await dbContext.TelegramChatSubscriptions
            .FirstOrDefaultAsync(item => item.ChatId == chatId, cancellationToken);

        if (subscription is null)
        {
            subscription = new TelegramChatSubscription
            {
                ChatId = chatId,
                ChatTitle = chatId,
                EnableAdvisoryPush = true,
                CreatedTime = DateTimeOffset.UtcNow,
                LastUpdatedTime = DateTimeOffset.UtcNow
            };
            dbContext.TelegramChatSubscriptions.Add(subscription);
        }

        var existing = ParseKeywords(subscription.AdvisoryKeywords);
        foreach (var keyword in keywords)
        {
            existing.Add(keyword);
        }

        subscription.AdvisoryKeywords = string.Join(", ", existing.Order(StringComparer.OrdinalIgnoreCase));
        subscription.EnableAdvisoryPush = true;
        subscription.LastUpdatedTime = DateTimeOffset.UtcNow;
        await dbContext.SaveChangesAsync(cancellationToken);

        return $"已更新弱點訂閱\n目前關鍵字: {subscription.AdvisoryKeywords}";
    }

    private async Task<string> BuildUnsubscribeReplyAsync(string? chatId, string? argument, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(chatId))
        {
            return "這個指令需要在 Telegram chat 裡使用。";
        }

        var subscription = await dbContext.TelegramChatSubscriptions
            .FirstOrDefaultAsync(item => item.ChatId == chatId, cancellationToken);

        if (subscription is null)
        {
            return "目前還沒有訂閱設定。";
        }

        var keywordsToRemove = ParseKeywords(argument);
        if (keywordsToRemove.Count == 0)
        {
            subscription.EnableAdvisoryPush = false;
            subscription.LastUpdatedTime = DateTimeOffset.UtcNow;
            await dbContext.SaveChangesAsync(cancellationToken);
            return "已暫停這個 chat 的弱點通知。";
        }

        var existing = ParseKeywords(subscription.AdvisoryKeywords);
        existing.ExceptWith(keywordsToRemove);
        subscription.AdvisoryKeywords = existing.Count == 0 ? null : string.Join(", ", existing.Order(StringComparer.OrdinalIgnoreCase));
        subscription.EnableAdvisoryPush = existing.Count > 0;
        subscription.LastUpdatedTime = DateTimeOffset.UtcNow;
        await dbContext.SaveChangesAsync(cancellationToken);

        return existing.Count == 0
            ? "已移除所有弱點訂閱關鍵字，並暫停弱點通知。"
            : $"已更新弱點訂閱\n目前關鍵字: {subscription.AdvisoryKeywords}";
    }

    private async Task<string> BuildWatchlistReplyAsync(string? chatId, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(chatId))
        {
            return "這個指令需要在 Telegram chat 裡使用。";
        }

        var subscription = await dbContext.TelegramChatSubscriptions
            .FirstOrDefaultAsync(item => item.ChatId == chatId, cancellationToken);

        if (subscription is null || string.IsNullOrWhiteSpace(subscription.AdvisoryKeywords))
        {
            return "目前沒有弱點訂閱關鍵字。可以用 /subscribe fortinet azure 加入。";
        }

        return $"弱點通知: {(subscription.EnableAdvisoryPush ? "開啟" : "暫停")}\n關鍵字: {subscription.AdvisoryKeywords}";
    }

    private async Task<string> BuildSmartFallbackReplyAsync(string normalized, CancellationToken cancellationToken)
    {
        if (ExtractCveId(normalized) is { } cveId)
        {
            return await BuildExplainReplyAsync(cveId, cancellationToken);
        }

        if (normalized.EndsWith('?') || normalized.Contains('？') || normalized.Length > 12)
        {
            return await answerService.BuildAnswerAsync(normalized, cancellationToken);
        }

        return BuildHelpReply();
    }

    private static string BuildAdvisoryListReply(string heading, IReadOnlyList<SecurityAdvisory> advisories)
    {
        if (advisories.Count == 0)
        {
            return $"{heading}\n目前沒有符合條件的資料。可以先執行 /sync。";
        }

        var builder = new StringBuilder();
        builder.AppendLine(heading);
        for (var index = 0; index < advisories.Count; index++)
        {
            var advisory = advisories[index];
            builder.AppendLine($"{index + 1}. {BuildShortTitle(advisory)}");
            builder.AppendLine($"   風險: {BuildRiskText(advisory)}");
            builder.AppendLine($"   摘要: {Trim(advisory.AiSummary ?? advisory.Description, 150)}");
        }

        return builder.ToString().TrimEnd();
    }

    private static string BuildAdvisoryDetailReply(SecurityAdvisory advisory)
    {
        var builder = new StringBuilder();
        builder.AppendLine(BuildShortTitle(advisory));
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
        builder.AppendLine($"來源: {advisory.SourceName} {advisory.SourceUrl}");
        return builder.ToString().TrimEnd();
    }

    private static IQueryable<SecurityAdvisory> ApplyKeywordFilter(IQueryable<SecurityAdvisory> advisories, string query)
    {
        var keywords = ParseKeywords(query).Take(6).ToList();
        foreach (var keyword in keywords)
        {
            var current = keyword;
            advisories = advisories.Where(advisory =>
                (advisory.CveId != null && advisory.CveId.ToLower().Contains(current)) ||
                advisory.Title.ToLower().Contains(current) ||
                advisory.Description.ToLower().Contains(current) ||
                (advisory.Vendor != null && advisory.Vendor.ToLower().Contains(current)) ||
                (advisory.Product != null && advisory.Product.ToLower().Contains(current)) ||
                (advisory.Tags != null && advisory.Tags.ToLower().Contains(current)));
        }

        return advisories;
    }

    private static string BuildShortTitle(SecurityAdvisory advisory)
        => string.IsNullOrWhiteSpace(advisory.CveId)
            ? advisory.Title
            : $"{advisory.CveId} - {advisory.Title}";

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

        if (advisory.HasRansomwareUse)
        {
            parts.Add("ransomware use");
        }

        return parts.Count == 0 ? "未標示" : string.Join(" / ", parts);
    }

    private static string BuildAffectedProduct(SecurityAdvisory advisory)
    {
        var values = new[] { advisory.Vendor, advisory.Product }.Where(value => !string.IsNullOrWhiteSpace(value));
        var text = string.Join(" / ", values);
        return string.IsNullOrWhiteSpace(text) ? "未標示" : text;
    }

    private static (string Command, string? Argument) SplitCommand(string normalized)
    {
        var parts = normalized.Split(' ', 2, StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        var command = parts[0].Split('@', 2)[0].ToLowerInvariant();
        var argument = parts.Length > 1 ? parts[1] : null;
        return (command, argument);
    }

    private static string NormalizeCommand(string commandText)
        => string.Join(' ', commandText.Split(['\r', '\n', '\t'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)).Trim();

    private static HashSet<string> ParseKeywords(string? value)
    {
        var result = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (string.IsNullOrWhiteSpace(value))
        {
            return result;
        }

        foreach (Match match in KeywordRegex().Matches(value.ToLowerInvariant()))
        {
            result.Add(match.Value);
        }

        return result;
    }

    private static string? ExtractCveId(string value)
    {
        var match = CveRegex().Match(value);
        return match.Success ? match.Value.ToUpperInvariant() : null;
    }

    private static string Trim(string value, int maxLength)
    {
        var compact = string.Join(' ', value.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));
        return compact.Length <= maxLength ? compact : compact[..maxLength] + "...";
    }

    private static string BuildHelpReply()
        => """
        Security Advisory Bot
        /latest - 最新弱點
        /critical - Critical 弱點
        /kev - CISA KEV 已知遭利用弱點
        /explain CVE-2024-3094 - 查看單一 CVE
        /ask 最近 Fortinet 有哪些風險？ - 用 RAG 查詢
        /subscribe fortinet azure windows - 訂閱關鍵字推播
        /watchlist - 查看目前訂閱
        /sync - 手動同步資料
        """;

    [GeneratedRegex("cve-\\d{4}-\\d{4,}", RegexOptions.IgnoreCase)]
    private static partial Regex CveRegex();

    [GeneratedRegex("[\\p{L}\\p{N}_.:-]{2,}")]
    private static partial Regex KeywordRegex();
}
