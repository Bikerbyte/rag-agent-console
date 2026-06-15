using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;
using Pgvector;

#nullable disable

namespace RagAgentConsole.Migrations
{
    /// <inheritdoc />
    public partial class RemoveSecurityAdvisoryFeatures : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                """
                UPDATE "KnowledgeDocuments"
                SET "ModuleName" = 'InternalDocs'
                WHERE "ModuleName" = 'CveAdvisory';

                DELETE FROM "AppSettings"
                WHERE "SettingKey" LIKE 'DataSources:%'
                   OR "SettingKey" LIKE 'PushNotifications:%'
                   OR "SettingKey" LIKE 'SecurityAdvisories:%'
                   OR "SettingKey" = 'Agent:DefaultDomain'
                   OR (
                       "SettingKey" = 'Agent:PlannerSystemPrompt'
                       AND (
                           "SettingValue" ILIKE '%CveAdvisory%'
                           OR "SettingValue" ILIKE '%security_advisory%'
                           OR "SettingValue" ILIKE '%CISA%'
                           OR "SettingValue" ILIKE '%NVD%'
                       )
                   );
                """);

            migrationBuilder.DropTable(
                name: "SecurityAdvisoryChunks");

            migrationBuilder.DropTable(
                name: "SyncJobLogs");

            migrationBuilder.DropTable(
                name: "SecurityAdvisories");

            migrationBuilder.DropColumn(
                name: "AdvisoryKeywords",
                table: "TelegramChatSubscriptions");

            migrationBuilder.DropColumn(
                name: "EnableAdvisoryPush",
                table: "TelegramChatSubscriptions");

            migrationBuilder.DropColumn(
                name: "MinimumSeverity",
                table: "TelegramChatSubscriptions");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "AdvisoryKeywords",
                table: "TelegramChatSubscriptions",
                type: "character varying(800)",
                maxLength: 800,
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "EnableAdvisoryPush",
                table: "TelegramChatSubscriptions",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "MinimumSeverity",
                table: "TelegramChatSubscriptions",
                type: "character varying(32)",
                maxLength: 32,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "SecurityAdvisories",
                columns: table => new
                {
                    SecurityAdvisoryId = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    AiSummary = table.Column<string>(type: "character varying(3000)", maxLength: 3000, nullable: true),
                    ContentHash = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    CreatedTime = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    CveId = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: true),
                    CvssScore = table.Column<decimal>(type: "numeric", nullable: true),
                    Description = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: false),
                    DueDate = table.Column<DateOnly>(type: "date", nullable: true),
                    ExternalId = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    HasRansomwareUse = table.Column<bool>(type: "boolean", nullable: false),
                    IsKnownExploited = table.Column<bool>(type: "boolean", nullable: false),
                    IsSent = table.Column<bool>(type: "boolean", nullable: false),
                    LastModifiedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    LastSyncedTime = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    Product = table.Column<string>(type: "character varying(160)", maxLength: 160, nullable: true),
                    PublishedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    RequiredAction = table.Column<string>(type: "character varying(1200)", maxLength: 1200, nullable: true),
                    Severity = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: true),
                    SourceName = table.Column<string>(type: "character varying(48)", maxLength: 48, nullable: false),
                    SourceUrl = table.Column<string>(type: "character varying(800)", maxLength: 800, nullable: false),
                    SuggestedAction = table.Column<string>(type: "character varying(1600)", maxLength: 1600, nullable: true),
                    Tags = table.Column<string>(type: "character varying(800)", maxLength: 800, nullable: true),
                    Title = table.Column<string>(type: "character varying(240)", maxLength: 240, nullable: false),
                    Vendor = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: true)
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
                    EndTime = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    InstanceName = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    IsSuccess = table.Column<bool>(type: "boolean", nullable: false),
                    JobName = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Message = table.Column<string>(type: "character varying(400)", maxLength: 400, nullable: true),
                    StartTime = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SyncJobLogs", x => x.SyncJobLogId);
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
                    CreatedTime = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    Embedding = table.Column<Vector>(type: "vector", nullable: true),
                    EmbeddingDimensions = table.Column<int>(type: "integer", nullable: false)
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
        }
    }
}
