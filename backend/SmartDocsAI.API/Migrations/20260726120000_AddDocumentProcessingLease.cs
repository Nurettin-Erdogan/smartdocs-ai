using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using SmartDocsAI.API.Data;

#nullable disable

namespace SmartDocsAI.API.Migrations;

[DbContext(typeof(AppDbContext))]
[Migration("20260726120000_AddDocumentProcessingLease")]
public partial class AddDocumentProcessingLease : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<DateTime>(
            name: "ProcessingStartedAt",
            table: "Documents",
            type: "timestamp with time zone",
            nullable: true);

        migrationBuilder.AddColumn<int>(
            name: "ProcessingAttemptCount",
            table: "Documents",
            type: "integer",
            nullable: false,
            defaultValue: 0);

        migrationBuilder.AddColumn<DateTime>(
            name: "NextProcessingAttemptAt",
            table: "Documents",
            type: "timestamp with time zone",
            nullable: true);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropColumn(
            name: "ProcessingStartedAt",
            table: "Documents");

        migrationBuilder.DropColumn(
            name: "ProcessingAttemptCount",
            table: "Documents");

        migrationBuilder.DropColumn(
            name: "NextProcessingAttemptAt",
            table: "Documents");
    }
}
