using System.Text.Json;
using RagAgentConsole.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Pgvector;

namespace RagAgentConsole.Data;

/// <summary>
/// 管理頁面、同步工作與 Telegram 推送紀錄共用的 EF Core DbContext。
/// </summary>
public class ApplicationDbContext(DbContextOptions<ApplicationDbContext> options) : DbContext(options)
{
    public DbSet<TelegramChatSubscription> TelegramChatSubscriptions => Set<TelegramChatSubscription>();
    public DbSet<TelegramUpdateInbox> TelegramUpdateInboxes => Set<TelegramUpdateInbox>();
    public DbSet<PushLog> PushLogs => Set<PushLog>();
    public DbSet<SyncJobLog> SyncJobLogs => Set<SyncJobLog>();
    public DbSet<SecurityAdvisory> SecurityAdvisories => Set<SecurityAdvisory>();
    public DbSet<SecurityAdvisoryChunk> SecurityAdvisoryChunks => Set<SecurityAdvisoryChunk>();
    public DbSet<KnowledgeDocument> KnowledgeDocuments => Set<KnowledgeDocument>();
    public DbSet<KnowledgeDocumentChunk> KnowledgeDocumentChunks => Set<KnowledgeDocumentChunk>();
    public DbSet<AppSetting> AppSettings => Set<AppSetting>();
    public DbSet<RetrievalEvaluationCaseEntity> RetrievalEvaluationCases => Set<RetrievalEvaluationCaseEntity>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        if (Database.IsRelational())
        {
            // 讓 migration 自帶 CREATE EXTENSION vector，部署時不需要手動準備。
            modelBuilder.HasPostgresExtension("vector");
        }
        else
        {
            // 單元測試用 in-memory provider 時沒有 vector 型別，退化成 JSON 字串存放。
            var vectorToJson = new Microsoft.EntityFrameworkCore.Storage.ValueConversion.ValueConverter<Vector, string>(
                vector => JsonSerializer.Serialize(vector.ToArray(), (JsonSerializerOptions?)null),
                json => new Vector(JsonSerializer.Deserialize<float[]>(json, (JsonSerializerOptions?)null) ?? Array.Empty<float>()));
            var vectorComparer = new ValueComparer<Vector?>(
                (left, right) => Equals(left, right),
                vector => vector == null ? 0 : vector.GetHashCode());

            modelBuilder.Entity<SecurityAdvisoryChunk>()
                .Property(chunk => chunk.Embedding)
                .HasConversion(vectorToJson!, vectorComparer);
            modelBuilder.Entity<KnowledgeDocumentChunk>()
                .Property(chunk => chunk.Embedding)
                .HasConversion(vectorToJson!, vectorComparer);
        }

        // 每個 Telegram chat 只保留一筆訂閱設定，後續管理和推播判斷會簡單很多。
        modelBuilder.Entity<TelegramChatSubscription>()
            .HasIndex(chatSubscription => chatSubscription.ChatId)
            .IsUnique();

        // 同一筆 Telegram update 只需要收一次，避免 webhook 重送或多節點重複入列。
        modelBuilder.Entity<TelegramUpdateInbox>()
            .HasIndex(updateInbox => updateInbox.UpdateId)
            .IsUnique();

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

        modelBuilder.Entity<RetrievalEvaluationCaseEntity>()
            .HasIndex(evaluationCase => evaluationCase.CaseKey)
            .IsUnique();

        modelBuilder.Entity<KnowledgeDocument>()
            .HasIndex(document => document.ModuleName);

        modelBuilder.Entity<KnowledgeDocument>()
            .HasMany(document => document.Chunks)
            .WithOne(chunk => chunk.Document)
            .HasForeignKey(chunk => chunk.KnowledgeDocumentId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
