using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SmartDocsAI.API.Migrations
{
    /// <inheritdoc />
    public partial class AddDocumentIndexingStatus : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "IndexingError",
                table: "Documents",
                type: "character varying(1000)",
                maxLength: 1000,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "IndexingStatus",
                table: "Documents",
                type: "character varying(20)",
                maxLength: 20,
                nullable: false,
                defaultValue: "");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "IndexingError",
                table: "Documents");

            migrationBuilder.DropColumn(
                name: "IndexingStatus",
                table: "Documents");
        }
    }
}
