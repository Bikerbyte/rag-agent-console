using CPBLLineBotCloud.Models;
using Microsoft.EntityFrameworkCore;

namespace CPBLLineBotCloud.Data;

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

        modelBuilder.Entity<TeamInfo>()
            .HasIndex(team => team.TeamCode)
            .IsUnique();

        modelBuilder.Entity<TelegramChatSubscription>()
            .HasIndex(chatSubscription => chatSubscription.ChatId)
            .IsUnique();
    }
}
