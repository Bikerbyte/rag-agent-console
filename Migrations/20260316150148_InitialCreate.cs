using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace CPBLLineBotCloud.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Games",
                columns: table => new
                {
                    GameInfoId = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    GameDate = table.Column<DateOnly>(type: "date", nullable: false),
                    StartTime = table.Column<TimeOnly>(type: "time without time zone", nullable: true),
                    HomeTeamCode = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    AwayTeamCode = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    HomeScore = table.Column<int>(type: "integer", nullable: true),
                    AwayScore = table.Column<int>(type: "integer", nullable: true),
                    Status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    InningText = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: true),
                    Venue = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    LastUpdatedTime = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Games", x => x.GameInfoId);
                });

            migrationBuilder.CreateTable(
                name: "LineGroupSubscriptions",
                columns: table => new
                {
                    LineGroupSubscriptionId = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    GroupId = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    GroupName = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    EnableSchedulePush = table.Column<bool>(type: "boolean", nullable: false),
                    EnableNewsPush = table.Column<bool>(type: "boolean", nullable: false),
                    EnableFubonOnly = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedTime = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    LastUpdatedTime = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_LineGroupSubscriptions", x => x.LineGroupSubscriptionId);
                });

            migrationBuilder.CreateTable(
                name: "NewsItems",
                columns: table => new
                {
                    NewsInfoId = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    Title = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    SourceName = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Url = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    PublishTime = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    Category = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: true),
                    Summary = table.Column<string>(type: "character varying(600)", maxLength: 600, nullable: true),
                    IsSent = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_NewsItems", x => x.NewsInfoId);
                });

            migrationBuilder.CreateTable(
                name: "PushLogs",
                columns: table => new
                {
                    PushLogId = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
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
                name: "SyncJobLogs",
                columns: table => new
                {
                    SyncJobLogId = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
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
                name: "Teams",
                columns: table => new
                {
                    TeamInfoId = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    TeamCode = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    TeamName = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    DisplayName = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Teams", x => x.TeamInfoId);
                });

            migrationBuilder.CreateIndex(
                name: "IX_LineGroupSubscriptions_GroupId",
                table: "LineGroupSubscriptions",
                column: "GroupId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Teams_TeamCode",
                table: "Teams",
                column: "TeamCode",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "Games");

            migrationBuilder.DropTable(
                name: "LineGroupSubscriptions");

            migrationBuilder.DropTable(
                name: "NewsItems");

            migrationBuilder.DropTable(
                name: "PushLogs");

            migrationBuilder.DropTable(
                name: "SyncJobLogs");

            migrationBuilder.DropTable(
                name: "Teams");
        }
    }
}
