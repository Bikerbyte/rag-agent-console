using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace RagAgentConsole.Migrations
{
    /// <inheritdoc />
    public partial class AddRetrievalEvaluationCases : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "RetrievalEvaluationCases",
                columns: table => new
                {
                    RetrievalEvaluationCaseId = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    CaseKey = table.Column<string>(type: "character varying(96)", maxLength: 96, nullable: false),
                    Question = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    ExpectedCveIds = table.Column<string>(type: "character varying(1200)", maxLength: 1200, nullable: true),
                    ExpectedDocumentTitles = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    Notes = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    IsSeeded = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedTime = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    LastUpdatedTime = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RetrievalEvaluationCases", x => x.RetrievalEvaluationCaseId);
                });

            migrationBuilder.CreateIndex(
                name: "IX_RetrievalEvaluationCases_CaseKey",
                table: "RetrievalEvaluationCases",
                column: "CaseKey",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "RetrievalEvaluationCases");
        }
    }
}
