using CPBLLineBotCloud.Models;
using Microsoft.EntityFrameworkCore;

namespace CPBLLineBotCloud.Data;

/// <summary>
/// 管理頁面、同步工作與 Telegram 推送紀錄共用的 EF Core DbContext。
/// </summary>
public class ApplicationDbContext(DbContextOptions<ApplicationDbContext> options) : DbContext(options)
{
    public DbSet<TeamInfo> Teams => Set<TeamInfo>();
    public DbSet<GameInfo> Games => Set<GameInfo>();
    public DbSet<NewsInfo> NewsItems => Set<NewsInfo>();
    public DbSet<TelegramChatSubscription> TelegramChatSubscriptions => Set<TelegramChatSubscription>();
    public DbSet<PushLog> PushLogs => Set<PushLog>();
    public DbSet<SyncJobLog> SyncJobLogs => Set<SyncJobLog>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        // TeamCode 會在同步、指令回覆和管理頁面共用，資料庫這邊直接維持唯一。
        modelBuilder.Entity<TeamInfo>()
            .HasIndex(team => team.TeamCode)
            .IsUnique();

        // 每個 Telegram chat 只保留一筆訂閱設定，後續管理和推播判斷會簡單很多。
        modelBuilder.Entity<TelegramChatSubscription>()
            .HasIndex(chatSubscription => chatSubscription.ChatId)
            .IsUnique();
    }
}
