using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MyLife.Migrations
{
    /// <inheritdoc />
    public partial class AddAuthProviderFlags : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "google_subject",
                table: "users",
                type: "character varying(255)",
                maxLength: 255,
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "has_google_provider",
                table: "users",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "has_local_provider",
                table: "users",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            // Preserve existing accounts: legacy Google-only records used this sentinel;
            // a local account that was previously overwritten to GOOGLE still retains its password.
            migrationBuilder.Sql("""
                UPDATE users
                SET has_local_provider = (auth_provider = 0 OR password_hash <> 'GOOGLE_OAUTH'),
                    has_google_provider = (auth_provider = 1);
                """);

            // Explicit-ID legacy seeds did not advance the identity sequences.
            migrationBuilder.Sql("""
                SELECT setval(pg_get_serial_sequence('users', 'id'), GREATEST((SELECT COALESCE(MAX(id), 0) + 1 FROM users), 1), false);
                SELECT setval(pg_get_serial_sequence('roles', 'id'), GREATEST((SELECT COALESCE(MAX(id), 0) + 1 FROM roles), 1), false);
                INSERT INTO roles (name) VALUES ('ADMIN'), ('USER') ON CONFLICT (name) DO NOTHING;
                INSERT INTO user_roles (user_id, role_id)
                SELECT DISTINCT ur.user_id, canonical.id
                FROM user_roles ur
                JOIN roles legacy ON legacy.id = ur.role_id
                JOIN roles canonical ON canonical.name = CASE WHEN lower(legacy.name) = 'admin' THEN 'ADMIN' ELSE 'USER' END
                WHERE lower(legacy.name) IN ('admin', 'user', 'member')
                ON CONFLICT (user_id, role_id) DO NOTHING;
                DELETE FROM user_roles ur USING roles legacy
                WHERE ur.role_id = legacy.id AND legacy.name NOT IN ('ADMIN', 'USER')
                  AND lower(legacy.name) IN ('admin', 'user', 'member');
                """);

            // Preserve active sessions while replacing legacy raw refresh credentials with SHA-256 hashes.
            migrationBuilder.Sql("UPDATE refresh_tokens SET token = upper(encode(sha256(convert_to(token, 'UTF8')), 'hex')) WHERE token !~ '^[A-F0-9]{64}$';");

            migrationBuilder.CreateIndex(
                name: "IX_users_google_subject",
                table: "users",
                column: "google_subject",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_users_google_subject",
                table: "users");

            migrationBuilder.DropColumn(
                name: "google_subject",
                table: "users");

            migrationBuilder.DropColumn(
                name: "has_google_provider",
                table: "users");

            migrationBuilder.DropColumn(
                name: "has_local_provider",
                table: "users");
        }
    }
}
