using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MyLife.Migrations;

public partial class AddFamilyMemberLocationSource : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<string>(
            name: "location_source",
            table: "family_members",
            type: "character varying(16)",
            maxLength: 16,
            nullable: true);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropColumn(name: "location_source", table: "family_members");
    }
}
