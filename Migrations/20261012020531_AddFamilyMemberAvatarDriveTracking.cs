using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MyLife.Migrations
{
    /// <inheritdoc />
    public partial class AddFamilyMemberAvatarDriveTracking : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "avatar_drive_file_id",
                table: "family_members",
                type: "character varying(255)",
                maxLength: 255,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "avatar_drive_file_id",
                table: "family_members");
        }
    }
}
