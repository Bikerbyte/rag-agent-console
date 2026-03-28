using CPBLLineBotCloud.Models;
using Microsoft.EntityFrameworkCore;

namespace CPBLLineBotCloud.Data;

public static class DemoDataSeeder
{
    private static readonly string[] LegacyDemoChatIds = ["-1001234567890", "998877665"];

    public static async Task SeedAsync(ApplicationDbContext dbContext, CancellationToken cancellationToken = default)
    {
        await RemoveLegacyDemoTelegramDataAsync(dbContext, cancellationToken);

        if (await dbContext.Teams.AnyAsync(cancellationToken))
        {
            return;
        }

        var now = DateTimeOffset.UtcNow;

        dbContext.Teams.AddRange(
            new TeamInfo { TeamCode = "FG", TeamName = "Fubon Guardians", DisplayName = "Fubon Guardians" },
            new TeamInfo { TeamCode = "UL", TeamName = "Uni-Lions", DisplayName = "Uni-Lions" },
            new TeamInfo { TeamCode = "CT", TeamName = "CTBC Brothers", DisplayName = "CTBC Brothers" },
            new TeamInfo { TeamCode = "RA", TeamName = "Rakuten Monkeys", DisplayName = "Rakuten Monkeys" },
            new TeamInfo { TeamCode = "WD", TeamName = "Wei Chuan Dragons", DisplayName = "Wei Chuan Dragons" },
            new TeamInfo { TeamCode = "TS", TeamName = "TSG Hawks", DisplayName = "TSG Hawks" });

        await dbContext.SaveChangesAsync(cancellationToken);
    }

    private static async Task RemoveLegacyDemoTelegramDataAsync(ApplicationDbContext dbContext, CancellationToken cancellationToken)
    {
        var demoSubscriptions = await dbContext.TelegramChatSubscriptions
            .Where(chat => LegacyDemoChatIds.Contains(chat.ChatId))
            .ToListAsync(cancellationToken);

        if (demoSubscriptions.Count > 0)
        {
            dbContext.TelegramChatSubscriptions.RemoveRange(demoSubscriptions);
        }

        var demoPushLogs = await dbContext.PushLogs
            .Where(log => LegacyDemoChatIds.Contains(log.TargetGroupId))
            .ToListAsync(cancellationToken);

        if (demoPushLogs.Count > 0)
        {
            dbContext.PushLogs.RemoveRange(demoPushLogs);
        }

        if (demoSubscriptions.Count > 0 || demoPushLogs.Count > 0)
        {
            await dbContext.SaveChangesAsync(cancellationToken);
        }
    }
}
