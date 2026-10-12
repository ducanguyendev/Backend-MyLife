using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace MyLife.Migrations
{
    /// <inheritdoc />
    public partial class AddFamilyAnniversaries : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "family_anniversaries",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    family_tree_id = table.Column<long>(type: "bigint", nullable: false),
                    related_member_id = table.Column<int>(type: "integer", nullable: true),
                    event_type = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    title = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    lunar_day = table.Column<int>(type: "integer", nullable: false),
                    lunar_month = table.Column<int>(type: "integer", nullable: false),
                    is_leap_month = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
                    place = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    host = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true),
                    notes = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP"),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_family_anniversaries", x => x.id);
                    table.ForeignKey(
                        name: "FK_family_anniversaries_family_members_related_member_id",
                        column: x => x.related_member_id,
                        principalTable: "family_members",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_family_anniversaries_family_trees_family_tree_id",
                        column: x => x.family_tree_id,
                        principalTable: "family_trees",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_family_anniversaries_family_tree_id",
                table: "family_anniversaries",
                column: "family_tree_id");

            migrationBuilder.CreateIndex(
                name: "IX_family_anniversaries_family_tree_id_lunar_month_lunar_day",
                table: "family_anniversaries",
                columns: new[] { "family_tree_id", "lunar_month", "lunar_day" });

            migrationBuilder.CreateIndex(
                name: "IX_family_anniversaries_related_member_id",
                table: "family_anniversaries",
                column: "related_member_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "family_anniversaries");
        }
    }
}
