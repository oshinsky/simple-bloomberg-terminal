using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using simple_bloomberg_terminal.Data;

#nullable disable

namespace simple_bloomberg_terminal.Migrations;

[DbContext(typeof(AppDbContext))]
[Migration("20260827120000_RemoveRevenueAndCostClassifications")]
public partial class RemoveRevenueAndCostClassifications : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropColumn(
            name: "CostBase",
            table: "CostSources");

        migrationBuilder.DropColumn(
            name: "SourceType",
            table: "RevenueSources");
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<int>(
            name: "CostBase",
            table: "CostSources",
            type: "int",
            nullable: false,
            defaultValue: 0);

        migrationBuilder.AddColumn<int>(
            name: "SourceType",
            table: "RevenueSources",
            type: "int",
            nullable: false,
            defaultValue: 0);
    }
}
