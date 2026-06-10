using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace RagAgentConsole.Migrations
{
    /// <inheritdoc />
    public partial class RemoveCveSpecificEvaluationFields : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            if (migrationBuilder.ActiveProvider == "Npgsql.EntityFrameworkCore.PostgreSQL")
            {
                migrationBuilder.Sql(
                    """
                    UPDATE "RetrievalEvaluationCases"
                    SET "ExpectedContentKeywords" = concat_ws(E'\n',
                        NULLIF("ExpectedContentKeywords", ''),
                        NULLIF("ExpectedCveIds", ''))
                    WHERE "ExpectedCveIds" IS NOT NULL AND "ExpectedCveIds" <> '';
                    """);
            }
            else if (migrationBuilder.ActiveProvider == "Microsoft.EntityFrameworkCore.SqlServer")
            {
                migrationBuilder.Sql(
                    """
                    UPDATE [RetrievalEvaluationCases]
                    SET [ExpectedContentKeywords] = CONCAT_WS(CHAR(10),
                        NULLIF([ExpectedContentKeywords], ''),
                        NULLIF([ExpectedCveIds], ''))
                    WHERE [ExpectedCveIds] IS NOT NULL AND [ExpectedCveIds] <> '';
                    """);
            }

            migrationBuilder.DropColumn(
                name: "ExpectedCveIds",
                table: "RetrievalEvaluationCases");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "ExpectedCveIds",
                table: "RetrievalEvaluationCases",
                type: "character varying(1200)",
                maxLength: 1200,
                nullable: true);
        }
    }
}
