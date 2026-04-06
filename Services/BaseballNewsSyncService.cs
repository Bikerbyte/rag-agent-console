using System.Globalization;
using System.Net;
using System.Text.RegularExpressions;
using CPBLLineBotCloud.Data;
using CPBLLineBotCloud.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace CPBLLineBotCloud.Services;

/// <summary>
/// 將 CPBL 官方網站的新聞同步到本機新聞資料表。
/// </summary>
public partial class BaseballNewsSyncService(
    ApplicationDbContext dbContext,
    IHttpClientFactory httpClientFactory,
    IOptions<DataSourceOptions> dataSourceOptions,
    IOptions<AppRuntimeOptions> runtimeOptions,
    ILogger<BaseballNewsSyncService> logger) : IBaseballNewsSyncService
{
    private static readonly TimeSpan FreshnessWindow = TimeSpan.FromMinutes(15);
    private readonly DataSourceOptions dataSourceOptions = dataSourceOptions.Value;

    public async Task<int> SyncAsync(CancellationToken cancellationToken = default)
    {
        var startedAt = DateTimeOffset.UtcNow;

        // 真實同步開始後，就把 demo seed 資料清掉，避免畫面混在一起。
        var demoRecords = await dbContext.NewsItems
            .Where(news => news.SourceName == "Local Demo Feed")
            .ToListAsync(cancellationToken);

        if (demoRecords.Count > 0)
        {
            dbContext.NewsItems.RemoveRange(demoRecords);
            await dbContext.SaveChangesAsync(cancellationToken);
        }

        if (await HasRecentSuccessfulSyncAsync(cancellationToken))
        {
            logger.LogInformation("Skipping baseball news sync because a recent official sync already exists.");
            return 0;
        }

        logger.LogInformation("Starting baseball news sync from official CPBL news list.");

        try
        {
            var officialNewsItems = await FetchOfficialNewsAsync(cancellationToken);
            var existingUrls = await dbContext.NewsItems
                .Select(news => news.Url)
                .ToListAsync(cancellationToken);

            var existingUrlSet = existingUrls.ToHashSet(StringComparer.OrdinalIgnoreCase);
            var addedCount = 0;

            foreach (var newsItem in officialNewsItems)
            {
                if (!existingUrlSet.Add(newsItem.Url))
                {
                    continue;
                }

                dbContext.NewsItems.Add(newsItem);
                addedCount++;
            }

            dbContext.SyncJobLogs.Add(new SyncJobLog
            {
                InstanceName = runtimeOptions.Value.InstanceName,
                JobName = "BaseballNewsSync",
                StartTime = startedAt,
                EndTime = DateTimeOffset.UtcNow,
                IsSuccess = true,
                Message = $"Official CPBL news sync completed. Added {addedCount} item(s)."
            });

            await dbContext.SaveChangesAsync(cancellationToken);
            logger.LogInformation("Finished baseball news sync. Added {AddedCount} item(s).", addedCount);

            return addedCount;
        }
        catch (Exception exception)
        {
            logger.LogError(exception, "Baseball news sync failed.");

            dbContext.SyncJobLogs.Add(new SyncJobLog
            {
                InstanceName = runtimeOptions.Value.InstanceName,
                JobName = "BaseballNewsSync",
                StartTime = startedAt,
                EndTime = DateTimeOffset.UtcNow,
                IsSuccess = false,
                Message = $"Official CPBL news sync failed: {exception.Message}"
            });

            await dbContext.SaveChangesAsync(cancellationToken);
            throw;
        }
    }

    private async Task<bool> HasRecentSuccessfulSyncAsync(CancellationToken cancellationToken)
    {
        var threshold = DateTimeOffset.UtcNow - FreshnessWindow;

        return await dbContext.SyncJobLogs.AnyAsync(
            log => log.JobName == "BaseballNewsSync" &&
                   log.IsSuccess &&
                   log.StartTime >= threshold &&
                   log.Message != null &&
                   EF.Functions.Like(log.Message, "%Official CPBL%"),
            cancellationToken);
    }

    private async Task<List<NewsInfo>> FetchOfficialNewsAsync(CancellationToken cancellationToken)
    {
        using var httpClient = httpClientFactory.CreateClient();
        var html = await httpClient.GetStringAsync(dataSourceOptions.BaseballNewsBaseUrl, cancellationToken);

        // 這段解析集中放在這裡即可，官方列表頁結構單純，用 regex 先處理是務實做法。
        var results = new List<NewsInfo>();

        foreach (Match match in NewsItemRegex().Matches(html))
        {
            var relativeUrl = DecodeHtmlText(match.Groups["url"].Value.Trim());
            var title = DecodeHtmlText(match.Groups["title"].Value.Trim());
            var publishDateText = match.Groups["date"].Value.Trim();
            var tagBlock = match.Groups["tags"].Value;

            if (string.IsNullOrWhiteSpace(relativeUrl) || string.IsNullOrWhiteSpace(title))
            {
                continue;
            }

            if (!DateTime.TryParseExact(
                    publishDateText,
                    "yyyy/MM/dd",
                    CultureInfo.InvariantCulture,
                    DateTimeStyles.None,
                    out var publishDate))
            {
                continue;
            }

            var publishTime = new DateTimeOffset(
                publishDate.Year,
                publishDate.Month,
                publishDate.Day,
                12,
                0,
                0,
                TimeSpan.FromHours(8)).ToUniversalTime();

            var tags = TagRegex().Matches(tagBlock)
                .Select(tagMatch => DecodeHtmlText(tagMatch.Groups["tag"].Value.Trim()))
                .Where(tag => !string.IsNullOrWhiteSpace(tag))
                .Take(3)
                .ToArray();

            results.Add(new NewsInfo
            {
                Title = title,
                SourceName = "CPBL Official Site",
                Url = new Uri(new Uri("https://www.cpbl.com.tw"), relativeUrl).ToString(),
                PublishTime = publishTime,
                Category = "\u8CFD\u4E8B\u65B0\u805E",
                Summary = tags.Length == 0 ? null : $"Tags: {string.Join(", ", tags)}",
                IsSent = false
            });
        }

        return results
            .OrderByDescending(news => news.PublishTime)
            .ThenBy(news => news.Title)
            .ToList();
    }

    private static string DecodeHtmlText(string value)
    {
        var current = value;

        for (var attempt = 0; attempt < 3; attempt++)
        {
            var decoded = WebUtility.HtmlDecode(current);
            if (string.Equals(decoded, current, StringComparison.Ordinal))
            {
                break;
            }

            current = decoded;
        }

        return current;
    }

    [GeneratedRegex("<div class=\"item\">.*?<a href=\"(?<url>/xmdoc/cont\\?sid=[^\"]+)\"[^>]*title=\"(?<title>[^\"]+)\">.*?</a>.*?<div class=\"date\">(?<date>\\d{4}/\\d{2}/\\d{2})</div>.*?<div class=\"tags\">(?<tags>.*?)</div>", RegexOptions.Singleline | RegexOptions.IgnoreCase)]
    private static partial Regex NewsItemRegex();

    [GeneratedRegex("<a [^>]*>(?<tag>.*?)</a>", RegexOptions.Singleline | RegexOptions.IgnoreCase)]
    private static partial Regex TagRegex();
}
