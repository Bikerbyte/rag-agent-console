using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace CPBLLineBotCloud.Migrations
{
    /// <inheritdoc />
    public partial class SwitchToTelegramDelivery : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "LineGroupSubscriptions");

            migrationBuilder.CreateTable(
                name: "TelegramChatSubscriptions",
                columns: table => new
                {
                    TelegramChatSubscriptionId = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    ChatId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    ChatTitle = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    EnableSchedulePush = table.Column<bool>(type: "boolean", nullable: false),
                    EnableNewsPush = table.Column<bool>(type: "boolean", nullable: false),
                    EnableFubonOnly = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedTime = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    LastUpdatedTime = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TelegramChatSubscriptions", x => x.TelegramChatSubscriptionId);
                });

            migrationBuilder.CreateIndex(
                name: "IX_TelegramChatSubscriptions_ChatId",
                table: "TelegramChatSubscriptions",
                column: "ChatId",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "TelegramChatSubscriptions");

            migrationBuilder.CreateTable(
                name: "LineGroupSubscriptions",
                columns: table => new
                {
                    LineGroupSubscriptionId = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    CreatedTime = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    EnableFubonOnly = table.Column<bool>(type: "boolean", nullable: false),
                    EnableNewsPush = table.Column<bool>(type: "boolean", nullable: false),
                    EnableSchedulePush = table.Column<bool>(type: "boolean", nullable: false),
                    GroupId = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    GroupName = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    LastUpdatedTime = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_LineGroupSubscriptions", x => x.LineGroupSubscriptionId);
                });

            migrationBuilder.CreateIndex(
                name: "IX_LineGroupSubscriptions_GroupId",
                table: "LineGroupSubscriptions",
                column: "GroupId",
                unique: true);
        }
    }
}
