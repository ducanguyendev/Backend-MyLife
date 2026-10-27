using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Infrastructure;
using MyLife.Shared.Data;

#nullable disable

namespace MyLife.Migrations;

[DbContext(typeof(AppDbContext))]
[Migration("20261027120000_MakeFamilyEventLunarFieldsOptional")]
public partial class MakeFamilyEventLunarFieldsOptional : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AlterColumn<int>(name: "lunar_day", table: "family_anniversaries", type: "integer", nullable: true, oldClrType: typeof(int), oldType: "integer");
        migrationBuilder.AlterColumn<int>(name: "lunar_month", table: "family_anniversaries", type: "integer", nullable: true, oldClrType: typeof(int), oldType: "integer");
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("UPDATE family_anniversaries SET lunar_day = 1 WHERE lunar_day IS NULL;");
        migrationBuilder.Sql("UPDATE family_anniversaries SET lunar_month = 1 WHERE lunar_month IS NULL;");
        migrationBuilder.AlterColumn<int>(name: "lunar_day", table: "family_anniversaries", type: "integer", nullable: false, oldClrType: typeof(int), oldType: "integer", oldNullable: true);
        migrationBuilder.AlterColumn<int>(name: "lunar_month", table: "family_anniversaries", type: "integer", nullable: false, oldClrType: typeof(int), oldType: "integer", oldNullable: true);
    }
}
