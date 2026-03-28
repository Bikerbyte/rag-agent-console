using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CPBLLineBotCloud.Migrations
{
    /// <inheritdoc />
    public partial class ReplaceFubonOnlyWithTrackedTeam : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "EnableFubonOnly",
                table: "TelegramChatSubscriptions");

            migrationBuilder.AddColumn<string>(
                name: "FollowedTeamCode",
                table: "TelegramChatSubscriptions",
                type: "character varying(16)",
                maxLength: 16,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "FollowedTeamCode",
                table: "TelegramChatSubscriptions");

            migrationBuilder.AddColumn<bool>(
                name: "EnableFubonOnly",
                table: "TelegramChatSubscriptions",
                type: "boolean",
                nullable: false,
                defaultValue: false);
        }
    }
}
