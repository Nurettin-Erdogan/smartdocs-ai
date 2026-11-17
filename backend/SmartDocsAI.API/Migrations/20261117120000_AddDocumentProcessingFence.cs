using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using SmartDocsAI.API.Data;

#nullable disable

namespace SmartDocsAI.API.Migrations;

[DbContext(typeof(AppDbContext))]
[Migration("20261117120000_AddDocumentProcessingFence")]
public partial class AddDocumentProcessingFence : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<Guid>(
            name: "ProcessingLeaseId",
            table: "Documents",
            type: "uuid",
            nullable: true);

        migrationBuilder.AddColumn<DateTime>(
            name: "ProcessingHeartbeatAt",
            table: "Documents",
            type: "timestamp with time zone",
            nullable: true);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropColumn(
            name: "ProcessingLeaseId",
            table: "Documents");

        migrationBuilder.DropColumn(
            name: "ProcessingHeartbeatAt",
            table: "Documents");
    }
}
