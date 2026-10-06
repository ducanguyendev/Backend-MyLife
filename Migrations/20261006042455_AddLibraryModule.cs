using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace MyLife.Migrations
{
    /// <inheritdoc />
    public partial class AddLibraryModule : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "library_albums",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    name = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: false),
                    description = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    drive_folder_id = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: true),
                    cover_photo_id = table.Column<long>(type: "bigint", nullable: true),
                    created_by_user_id = table.Column<int>(type: "integer", nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP"),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_library_albums", x => x.id);
                    table.ForeignKey(
                        name: "FK_library_albums_users_created_by_user_id",
                        column: x => x.created_by_user_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "library_photos",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    album_id = table.Column<long>(type: "bigint", nullable: false),
                    drive_file_id = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    url = table.Column<string>(type: "text", nullable: false),
                    file_name = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    content_type = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    file_size = table.Column<long>(type: "bigint", nullable: false),
                    caption = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    sort_order = table.Column<int>(type: "integer", nullable: false, defaultValue: 0),
                    taken_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_library_photos", x => x.id);
                    table.ForeignKey(
                        name: "FK_library_photos_library_albums_album_id",
                        column: x => x.album_id,
                        principalTable: "library_albums",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_library_albums_cover_photo_id",
                table: "library_albums",
                column: "cover_photo_id");

            migrationBuilder.CreateIndex(
                name: "IX_library_albums_created_by_user_id_updated_at",
                table: "library_albums",
                columns: new[] { "created_by_user_id", "updated_at" });

            migrationBuilder.CreateIndex(
                name: "IX_library_albums_drive_folder_id",
                table: "library_albums",
                column: "drive_folder_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_library_photos_album_id_sort_order_created_at",
                table: "library_photos",
                columns: new[] { "album_id", "sort_order", "created_at" });

            migrationBuilder.CreateIndex(
                name: "IX_library_photos_drive_file_id",
                table: "library_photos",
                column: "drive_file_id",
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "FK_library_albums_library_photos_cover_photo_id",
                table: "library_albums",
                column: "cover_photo_id",
                principalTable: "library_photos",
                principalColumn: "id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_library_albums_library_photos_cover_photo_id",
                table: "library_albums");

            migrationBuilder.DropTable(
                name: "library_photos");

            migrationBuilder.DropTable(
                name: "library_albums");
        }
    }
}
