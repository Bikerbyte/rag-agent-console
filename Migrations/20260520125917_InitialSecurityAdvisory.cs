using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace SecurityAdvisoryBot.Migrations
{
    /// <inheritdoc />
    public partial class InitialSecurityAdvisory : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
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
                name: "RuntimeLeadershipLeases",
                columns: table => new
                {
                    LeaseName = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    OwnerInstanceName = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    AcquiredAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    RenewedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ExpiresAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    LastUpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RuntimeLeadershipLeases", x => x.LeaseName);
                });

            migrationBuilder.CreateTable(
                name: "RuntimeNodeHeartbeats",
                columns: table => new
                {
                    RuntimeNodeHeartbeatId = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    InstanceName = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    MachineName = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    EnvironmentName = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    RoleSummary = table.Column<string>(type: "character varying(400)", maxLength: 400, nullable: false),
                    Status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    ProcessId = table.Column<int>(type: "integer", nullable: false),
                    ProcessStartedTime = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    LastSeenTime = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    AppVersion = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RuntimeNodeHeartbeats", x => x.RuntimeNodeHeartbeatId);
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
                name: "SecurityAdvisoryChunks",
                columns: table => new
                {
                    SecurityAdvisoryChunkId = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    SecurityAdvisoryId = table.Column<int>(type: "integer", nullable: false),
                    ChunkKind = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    ChunkText = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: false),
                    EmbeddingJson = table.Column<string>(type: "text", nullable: false),
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
                name: "IX_RuntimeNodeHeartbeats_InstanceName",
                table: "RuntimeNodeHeartbeats",
                column: "InstanceName",
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
                name: "PushLogs");

            migrationBuilder.DropTable(
                name: "RuntimeLeadershipLeases");

            migrationBuilder.DropTable(
                name: "RuntimeNodeHeartbeats");

            migrationBuilder.DropTable(
                name: "SecurityAdvisoryChunks");

            migrationBuilder.DropTable(
                name: "SyncJobLogs");

            migrationBuilder.DropTable(
                name: "TelegramChatSubscriptions");

            migrationBuilder.DropTable(
                name: "TelegramUpdateInboxes");

            migrationBuilder.DropTable(
                name: "SecurityAdvisories");
        }
    }
}
