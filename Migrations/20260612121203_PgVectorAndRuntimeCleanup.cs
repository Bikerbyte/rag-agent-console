using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;
using Pgvector;

#nullable disable

namespace RagAgentConsole.Migrations
{
    /// <summary>
    /// 升級既有部署：移除自製多節點協調表、改用原生 pgvector 欄位。
    /// 舊的 EmbeddingJson 向量是 ASCII-only 斷詞器產的，刻意不轉換直接清空——
    /// 升級後需全量重嵌（advisory 走重新同步、knowledge document 走後台重新索引）。
    /// </summary>
    public partial class PgVectorAndRuntimeCleanup : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "RuntimeLeadershipLeases");

            migrationBuilder.DropTable(
                name: "RuntimeNodeHeartbeats");

            migrationBuilder.DropColumn(
                name: "EmbeddingJson",
                table: "SecurityAdvisoryChunks");

            migrationBuilder.DropColumn(
                name: "EmbeddingJson",
                table: "KnowledgeDocumentChunks");

            migrationBuilder.AlterDatabase()
                .Annotation("Npgsql:PostgresExtension:vector", ",,");

            migrationBuilder.AddColumn<Vector>(
                name: "Embedding",
                table: "SecurityAdvisoryChunks",
                type: "vector",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "EmbeddingDimensions",
                table: "SecurityAdvisoryChunks",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<Vector>(
                name: "Embedding",
                table: "KnowledgeDocumentChunks",
                type: "vector",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "EmbeddingDimensions",
                table: "KnowledgeDocumentChunks",
                type: "integer",
                nullable: false,
                defaultValue: 0);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Embedding",
                table: "SecurityAdvisoryChunks");

            migrationBuilder.DropColumn(
                name: "EmbeddingDimensions",
                table: "SecurityAdvisoryChunks");

            migrationBuilder.DropColumn(
                name: "Embedding",
                table: "KnowledgeDocumentChunks");

            migrationBuilder.DropColumn(
                name: "EmbeddingDimensions",
                table: "KnowledgeDocumentChunks");

            migrationBuilder.AlterDatabase()
                .OldAnnotation("Npgsql:PostgresExtension:vector", ",,");

            migrationBuilder.AddColumn<string>(
                name: "EmbeddingJson",
                table: "SecurityAdvisoryChunks",
                type: "text",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "EmbeddingJson",
                table: "KnowledgeDocumentChunks",
                type: "text",
                nullable: false,
                defaultValue: "");

            migrationBuilder.CreateTable(
                name: "RuntimeLeadershipLeases",
                columns: table => new
                {
                    LeaseName = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    AcquiredAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ExpiresAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    LastUpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    OwnerInstanceName = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    RenewedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
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
                    AppVersion = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    EnvironmentName = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    InstanceName = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    LastSeenTime = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    MachineName = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    ProcessId = table.Column<int>(type: "integer", nullable: false),
                    ProcessStartedTime = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    RoleSummary = table.Column<string>(type: "character varying(400)", maxLength: 400, nullable: false),
                    Status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RuntimeNodeHeartbeats", x => x.RuntimeNodeHeartbeatId);
                });

            migrationBuilder.CreateIndex(
                name: "IX_RuntimeNodeHeartbeats_InstanceName",
                table: "RuntimeNodeHeartbeats",
                column: "InstanceName",
                unique: true);
        }
    }
}
