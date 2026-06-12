using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;
using Pgvector;

#nullable disable

namespace RagAgentConsole.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterDatabase()
                .Annotation("Npgsql:PostgresExtension:vector", ",,");

            migrationBuilder.CreateTable(
                name: "AppSettings",
                columns: table => new
                {
                    AppSettingId = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    SettingKey = table.Column<string>(type: "text", nullable: false),
                    SettingValue = table.Column<string>(type: "text", nullable: true),
                    IsSecret = table.Column<bool>(type: "boolean", nullable: false),
                    UpdatedTime = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AppSettings", x => x.AppSettingId);
                });

            migrationBuilder.CreateTable(
                name: "KnowledgeDocuments",
                columns: table => new
                {
                    KnowledgeDocumentId = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    ModuleName = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Title = table.Column<string>(type: "character varying(160)", maxLength: 160, nullable: false),
                    SourceType = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    FileName = table.Column<string>(type: "character varying(260)", maxLength: 260, nullable: true),
                    ContentType = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: true),
                    Vendor = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: true),
                    Product = table.Column<string>(type: "character varying(160)", maxLength: 160, nullable: true),
                    Tags = table.Column<string>(type: "character varying(800)", maxLength: 800, nullable: true),
                    ExtractedText = table.Column<string>(type: "text", nullable: false),
                    ContentHash = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    Status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    IsEnabled = table.Column<bool>(type: "boolean", nullable: false),
                    CharacterCount = table.Column<int>(type: "integer", nullable: false),
                    ChunkCount = table.Column<int>(type: "integer", nullable: false),
                    CreatedTime = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    LastUpdatedTime = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_KnowledgeDocuments", x => x.KnowledgeDocumentId);
                });

            migrationBuilder.CreateTable(
                name: "PushLogs",
                columns: table => new
                {
                    PushLogId = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    InstanceName = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    PushType = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    TargetGroupId = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    MessageTitle = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    IsSuccess = table.Column<bool>(type: "boolean", nullable: false),
                    ErrorMessage = table.Column<string>(type: "character varying(400)", maxLength: 400, nullable: true),
                    CreatedTime = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PushLogs", x => x.PushLogId);
                });

            migrationBuilder.CreateTable(
                name: "RetrievalEvaluationCases",
                columns: table => new
                {
                    RetrievalEvaluationCaseId = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    CaseKey = table.Column<string>(type: "character varying(96)", maxLength: 96, nullable: false),
                    Question = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    ExpectedDocumentTitles = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    ExpectedContentKeywords = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    ExpectedMetadata = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    Notes = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    IsSeeded = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedTime = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    LastUpdatedTime = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RetrievalEvaluationCases", x => x.RetrievalEvaluationCaseId);
                });

            migrationBuilder.CreateTable(
                name: "SecurityAdvisories",
                columns: table => new
                {
                    SecurityAdvisoryId = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    SourceName = table.Column<string>(type: "character varying(48)", maxLength: 48, nullable: false),
                    ExternalId = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    CveId = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: true),
                    Title = table.Column<string>(type: "character varying(240)", maxLength: 240, nullable: false),
                    Description = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: false),
                    Vendor = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: true),
                    Product = table.Column<string>(type: "character varying(160)", maxLength: 160, nullable: true),
                    Severity = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: true),
                    CvssScore = table.Column<decimal>(type: "numeric", nullable: true),
                    IsKnownExploited = table.Column<bool>(type: "boolean", nullable: false),
                    HasRansomwareUse = table.Column<bool>(type: "boolean", nullable: false),
                    RequiredAction = table.Column<string>(type: "character varying(1200)", maxLength: 1200, nullable: true),
                    DueDate = table.Column<DateOnly>(type: "date", nullable: true),
                    PublishedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    LastModifiedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    SourceUrl = table.Column<string>(type: "character varying(800)", maxLength: 800, nullable: false),
                    AiSummary = table.Column<string>(type: "character varying(3000)", maxLength: 3000, nullable: true),
                    SuggestedAction = table.Column<string>(type: "character varying(1600)", maxLength: 1600, nullable: true),
                    Tags = table.Column<string>(type: "character varying(800)", maxLength: 800, nullable: true),
                    ContentHash = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    IsSent = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedTime = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    LastSyncedTime = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SecurityAdvisories", x => x.SecurityAdvisoryId);
                });

            migrationBuilder.CreateTable(
                name: "SyncJobLogs",
                columns: table => new
                {
                    SyncJobLogId = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    InstanceName = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    JobName = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    StartTime = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    EndTime = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    IsSuccess = table.Column<bool>(type: "boolean", nullable: false),
                    Message = table.Column<string>(type: "character varying(400)", maxLength: 400, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SyncJobLogs", x => x.SyncJobLogId);
                });

            migrationBuilder.CreateTable(
                name: "TelegramChatSubscriptions",
                columns: table => new
                {
                    TelegramChatSubscriptionId = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    ChatId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    ChatTitle = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    EnableAdvisoryPush = table.Column<bool>(type: "boolean", nullable: false),
                    AdvisoryKeywords = table.Column<string>(type: "character varying(800)", maxLength: 800, nullable: true),
                    MinimumSeverity = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: true),
                    CreatedTime = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    LastUpdatedTime = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TelegramChatSubscriptions", x => x.TelegramChatSubscriptionId);
                });

            migrationBuilder.CreateTable(
                name: "TelegramUpdateInboxes",
                columns: table => new
                {
                    TelegramUpdateInboxId = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    UpdateId = table.Column<long>(type: "bigint", nullable: false),
                    SourceType = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    Status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    PayloadJson = table.Column<string>(type: "text", nullable: false),
                    IngressNode = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    ProcessingNode = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    AttemptCount = table.Column<int>(type: "integer", nullable: false),
                    EnqueuedTime = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ProcessingStartedTime = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    LeaseUntil = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    ProcessedTime = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    LastUpdatedTime = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    LastError = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TelegramUpdateInboxes", x => x.TelegramUpdateInboxId);
                });

            migrationBuilder.CreateTable(
                name: "KnowledgeDocumentChunks",
                columns: table => new
                {
                    KnowledgeDocumentChunkId = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    KnowledgeDocumentId = table.Column<int>(type: "integer", nullable: false),
                    ChunkKind = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    ChunkIndex = table.Column<int>(type: "integer", nullable: false),
                    ChunkText = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: false),
                    Embedding = table.Column<Vector>(type: "vector", nullable: true),
                    EmbeddingDimensions = table.Column<int>(type: "integer", nullable: false),
                    CreatedTime = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_KnowledgeDocumentChunks", x => x.KnowledgeDocumentChunkId);
                    table.ForeignKey(
                        name: "FK_KnowledgeDocumentChunks_KnowledgeDocuments_KnowledgeDocumen~",
                        column: x => x.KnowledgeDocumentId,
                        principalTable: "KnowledgeDocuments",
                        principalColumn: "KnowledgeDocumentId",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "SecurityAdvisoryChunks",
                columns: table => new
                {
                    SecurityAdvisoryChunkId = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    SecurityAdvisoryId = table.Column<int>(type: "integer", nullable: false),
                    ChunkKind = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    ChunkText = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: false),
                    Embedding = table.Column<Vector>(type: "vector", nullable: true),
                    EmbeddingDimensions = table.Column<int>(type: "integer", nullable: false),
                    CreatedTime = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SecurityAdvisoryChunks", x => x.SecurityAdvisoryChunkId);
                    table.ForeignKey(
                        name: "FK_SecurityAdvisoryChunks_SecurityAdvisories_SecurityAdvisoryId",
                        column: x => x.SecurityAdvisoryId,
                        principalTable: "SecurityAdvisories",
                        principalColumn: "SecurityAdvisoryId",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_AppSettings_SettingKey",
                table: "AppSettings",
                column: "SettingKey",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_KnowledgeDocumentChunks_KnowledgeDocumentId",
                table: "KnowledgeDocumentChunks",
                column: "KnowledgeDocumentId");

            migrationBuilder.CreateIndex(
                name: "IX_KnowledgeDocuments_ModuleName",
                table: "KnowledgeDocuments",
                column: "ModuleName");

            migrationBuilder.CreateIndex(
                name: "IX_RetrievalEvaluationCases_CaseKey",
                table: "RetrievalEvaluationCases",
                column: "CaseKey",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_SecurityAdvisories_CveId",
                table: "SecurityAdvisories",
                column: "CveId");

            migrationBuilder.CreateIndex(
                name: "IX_SecurityAdvisories_SourceName_ExternalId",
                table: "SecurityAdvisories",
                columns: new[] { "SourceName", "ExternalId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_SecurityAdvisoryChunks_SecurityAdvisoryId",
                table: "SecurityAdvisoryChunks",
                column: "SecurityAdvisoryId");

            migrationBuilder.CreateIndex(
                name: "IX_TelegramChatSubscriptions_ChatId",
                table: "TelegramChatSubscriptions",
                column: "ChatId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_TelegramUpdateInboxes_UpdateId",
                table: "TelegramUpdateInboxes",
                column: "UpdateId",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AppSettings");

            migrationBuilder.DropTable(
                name: "KnowledgeDocumentChunks");

            migrationBuilder.DropTable(
                name: "PushLogs");

            migrationBuilder.DropTable(
                name: "RetrievalEvaluationCases");

            migrationBuilder.DropTable(
                name: "SecurityAdvisoryChunks");

            migrationBuilder.DropTable(
                name: "SyncJobLogs");

            migrationBuilder.DropTable(
                name: "TelegramChatSubscriptions");

            migrationBuilder.DropTable(
                name: "TelegramUpdateInboxes");

            migrationBuilder.DropTable(
                name: "KnowledgeDocuments");

            migrationBuilder.DropTable(
                name: "SecurityAdvisories");
        }
    }
}
