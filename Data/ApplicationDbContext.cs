using SecurityAdvisoryBot.Models;
using Microsoft.EntityFrameworkCore;

namespace SecurityAdvisoryBot.Data;

/// <summary>
/// 管理頁面、同步工作與 Telegram 推送紀錄共用的 EF Core DbContext。
/// </summary>
public class ApplicationDbContext(DbContextOptions<ApplicationDbContext> options) : DbContext(options)
{
    public DbSet<RuntimeLeadershipLease> RuntimeLeadershipLeases => Set<RuntimeLeadershipLease>();
    public DbSet<RuntimeNodeHeartbeat> RuntimeNodeHeartbeats => Set<RuntimeNodeHeartbeat>();
    public DbSet<TelegramChatSubscription> TelegramChatSubscriptions => Set<TelegramChatSubscription>();
    public DbSet<TelegramUpdateInbox> TelegramUpdateInboxes => Set<TelegramUpdateInbox>();
    public DbSet<PushLog> PushLogs => Set<PushLog>();
    public DbSet<SyncJobLog> SyncJobLogs => Set<SyncJobLog>();
    public DbSet<SecurityAdvisory> SecurityAdvisories => Set<SecurityAdvisory>();
    public DbSet<SecurityAdvisoryChunk> SecurityAdvisoryChunks => Set<SecurityAdvisoryChunk>();
    public DbSet<KnowledgeDocument> KnowledgeDocuments => Set<KnowledgeDocument>();
    public DbSet<KnowledgeDocumentChunk> KnowledgeDocumentChunks => Set<KnowledgeDocumentChunk>();
    public DbSet<AppSetting> AppSettings => Set<AppSetting>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        // 每個 Telegram chat 只保留一筆訂閱設定，後續管理和推播判斷會簡單很多。
        modelBuilder.Entity<TelegramChatSubscription>()
            .HasIndex(chatSubscription => chatSubscription.ChatId)
            .IsUnique();

        // 同一筆 Telegram update 只需要收一次，避免 webhook 重送或多節點重複入列。
        modelBuilder.Entity<TelegramUpdateInbox>()
            .HasIndex(updateInbox => updateInbox.UpdateId)
            .IsUnique();

        // 後台要集中顯示節點狀態，所以 instance name 也保持唯一。
        modelBuilder.Entity<RuntimeNodeHeartbeat>()
            .HasIndex(heartbeat => heartbeat.InstanceName)
            .IsUnique();

        // 每種 scheduled job 的租約名稱只保留一筆，讓多節點只會有一個有效持有者。
        modelBuilder.Entity<RuntimeLeadershipLease>()
            .HasKey(lease => lease.LeaseName);

        modelBuilder.Entity<AppSetting>()
            .HasIndex(setting => setting.SettingKey)
            .IsUnique();

        modelBuilder.Entity<SecurityAdvisory>()
            .HasIndex(advisory => new { advisory.SourceName, advisory.ExternalId })
            .IsUnique();

        modelBuilder.Entity<SecurityAdvisory>()
            .HasIndex(advisory => advisory.CveId);

        modelBuilder.Entity<SecurityAdvisory>()
            .HasMany(advisory => advisory.Chunks)
            .WithOne(chunk => chunk.Advisory)
            .HasForeignKey(chunk => chunk.SecurityAdvisoryId)
            .OnDelete(DeleteBehavior.Cascade);

        modelBuilder.Entity<KnowledgeDocument>()
            .HasIndex(document => document.ModuleName);

        modelBuilder.Entity<KnowledgeDocument>()
            .HasMany(document => document.Chunks)
            .WithOne(chunk => chunk.Document)
            .HasForeignKey(chunk => chunk.KnowledgeDocumentId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
