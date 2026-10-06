using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MyLife.Migrations
{
    /// <inheritdoc />
    public partial class AddAvatarDriveTracking : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "avatar_drive_file_id",
                table: "users",
                type: "character varying(255)",
                maxLength: 255,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "avatar_source",
                table: "users",
                type: "character varying(20)",
                maxLength: 20,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "google_avatar_source_url",
                table: "users",
                type: "text",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "avatar_drive_file_id",
                table: "users");

            migrationBuilder.DropColumn(
                name: "avatar_source",
                table: "users");

            migrationBuilder.DropColumn(
                name: "google_avatar_source_url",
                table: "users");
        }
    }
}
