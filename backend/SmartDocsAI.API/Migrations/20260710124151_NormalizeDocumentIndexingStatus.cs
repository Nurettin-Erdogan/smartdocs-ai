using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SmartDocsAI.API.Migrations
{
    /// <inheritdoc />
    public partial class NormalizeDocumentIndexingStatus : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<string>(
                name: "IndexingStatus",
                table: "Documents",
                type: "character varying(20)",
                maxLength: 20,
                nullable: false,
                defaultValue: "Pending",
                oldClrType: typeof(string),
                oldType: "character varying(20)",
                oldMaxLength: 20);

            migrationBuilder.Sql(
                "UPDATE \"Documents\" SET \"IndexingStatus\" = 'Pending' WHERE \"IndexingStatus\" = '';");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<string>(
                name: "IndexingStatus",
                table: "Documents",
                type: "character varying(20)",
                maxLength: 20,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(20)",
                oldMaxLength: 20,
                oldDefaultValue: "Pending");
        }
    }
}
