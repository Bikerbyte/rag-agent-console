using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CPBLLineBotCloud.Migrations
{
    /// <inheritdoc />
    public partial class AddLiveScoreTrackingFields : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "PreviousAwayScore",
                table: "Games",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "PreviousHomeScore",
                table: "Games",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "PreviousStatus",
                table: "Games",
                type: "character varying(32)",
                maxLength: 32,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "PreviousAwayScore",
                table: "Games");

            migrationBuilder.DropColumn(
                name: "PreviousHomeScore",
                table: "Games");

            migrationBuilder.DropColumn(
                name: "PreviousStatus",
                table: "Games");
        }
    }
}
