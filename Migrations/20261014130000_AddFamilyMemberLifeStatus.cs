using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MyLife.Migrations;

public partial class AddFamilyMemberLifeStatus : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<string>(
            name: "life_status",
            table: "family_members",
            type: "character varying(16)",
            maxLength: 16,
            nullable: false,
            defaultValue: "LIVING");

        migrationBuilder.AddColumn<DateOnly>(
            name: "death_date",
            table: "family_members",
            type: "date",
            nullable: true);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropColumn(name: "life_status", table: "family_members");
        migrationBuilder.DropColumn(name: "death_date", table: "family_members");
    }
}
