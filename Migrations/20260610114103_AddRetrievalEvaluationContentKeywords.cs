using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace RagAgentConsole.Migrations
{
    /// <inheritdoc />
    public partial class AddRetrievalEvaluationContentKeywords : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "ExpectedContentKeywords",
                table: "RetrievalEvaluationCases",
                type: "character varying(2000)",
                maxLength: 2000,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ExpectedContentKeywords",
                table: "RetrievalEvaluationCases");
        }
    }
}
