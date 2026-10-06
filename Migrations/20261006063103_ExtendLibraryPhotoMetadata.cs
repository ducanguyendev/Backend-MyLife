using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MyLife.Migrations
{
    /// <inheritdoc />
    public partial class ExtendLibraryPhotoMetadata : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "author",
                table: "library_photos",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "category",
                table: "library_photos",
                type: "character varying(20)",
                maxLength: 20,
                nullable: false,
                defaultValue: "photos");

            migrationBuilder.AddColumn<string>(
                name: "display_date",
                table: "library_photos",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "title",
                table: "library_photos",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "author",
                table: "library_photos");

            migrationBuilder.DropColumn(
                name: "category",
                table: "library_photos");

            migrationBuilder.DropColumn(
                name: "display_date",
                table: "library_photos");

            migrationBuilder.DropColumn(
                name: "title",
                table: "library_photos");
        }
    }
}
