using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using SmartDocsAI.API.Data;

#nullable disable

namespace SmartDocsAI.API.Migrations;

[DbContext(typeof(AppDbContext))]
[Migration("20260714190000_AddCurrentIndexVersion")]
public partial class AddCurrentIndexVersion : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<string>(
            name: "CurrentIndexVersion",
            table: "Documents",
            type: "character varying(32)",
            maxLength: 32,
            nullable: true);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropColumn(
            name: "CurrentIndexVersion",
            table: "Documents");
    }
}
