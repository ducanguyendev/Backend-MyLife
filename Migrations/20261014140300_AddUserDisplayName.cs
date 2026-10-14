using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MyLife.Migrations
{
    /// <inheritdoc />
    public partial class AddUserDisplayName : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "display_name",
                table: "users",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true);

            // Preserve each account's currently visible name instead of replacing it with a generated short name.
            migrationBuilder.Sql("UPDATE users SET display_name = LEFT(COALESCE(NULLIF(BTRIM(full_name), ''), NULLIF(SPLIT_PART(email, '@', 1), ''), 'Account'), 100) WHERE display_name IS NULL OR BTRIM(display_name) = '';");

            migrationBuilder.AlterColumn<string>(
                name: "display_name",
                table: "users",
                type: "character varying(100)",
                maxLength: 100,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(100)",
                oldMaxLength: 100,
                oldNullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "display_name",
                table: "users");
        }
    }
}
