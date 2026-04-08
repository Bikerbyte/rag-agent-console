using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace CPBLLineBotCloud.Migrations
{
    /// <inheritdoc />
    public partial class AddRuntimeNodeHeartbeatAndNodeTracing : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "IngressNode",
                table: "TelegramUpdateInboxes",
                type: "character varying(128)",
                maxLength: 128,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "InstanceName",
                table: "SyncJobLogs",
                type: "character varying(128)",
                maxLength: 128,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "InstanceName",
                table: "PushLogs",
                type: "character varying(128)",
                maxLength: 128,
                nullable: true);

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

            migrationBuilder.CreateIndex(
                name: "IX_RuntimeNodeHeartbeats_InstanceName",
                table: "RuntimeNodeHeartbeats",
                column: "InstanceName",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "RuntimeNodeHeartbeats");

            migrationBuilder.DropColumn(
                name: "IngressNode",
                table: "TelegramUpdateInboxes");

            migrationBuilder.DropColumn(
                name: "InstanceName",
                table: "SyncJobLogs");

            migrationBuilder.DropColumn(
                name: "InstanceName",
                table: "PushLogs");
        }
    }
}
