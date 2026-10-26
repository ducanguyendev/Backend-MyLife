using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace MyLife.Migrations
{
    /// <inheritdoc />
    public partial class AddFamilyTimelineEvents : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "family_timeline_events",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    family_tree_id = table.Column<long>(type: "bigint", nullable: false),
                    member_id = table.Column<int>(type: "integer", nullable: true),
                    event_type = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    title = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    description = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    event_date = table.Column<DateOnly>(type: "date", nullable: true),
                    event_year = table.Column<int>(type: "integer", nullable: true),
                    is_approximate = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
                    location = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    latitude = table.Column<double>(type: "double precision", nullable: true),
                    longitude = table.Column<double>(type: "double precision", nullable: true),
                    related_member_id = table.Column<int>(type: "integer", nullable: true),
                    source_type = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: true),
                    source_id = table.Column<long>(type: "bigint", nullable: true),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP"),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_family_timeline_events", x => x.id);
                    table.ForeignKey(
                        name: "FK_family_timeline_events_family_members_member_id",
                        column: x => x.member_id,
                        principalTable: "family_members",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_family_timeline_events_family_members_related_member_id",
                        column: x => x.related_member_id,
                        principalTable: "family_members",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_family_timeline_events_family_trees_family_tree_id",
                        column: x => x.family_tree_id,
                        principalTable: "family_trees",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_family_timeline_events_family_tree_id_event_date_event_year",
                table: "family_timeline_events",
                columns: new[] { "family_tree_id", "event_date", "event_year" });

            migrationBuilder.CreateIndex(
                name: "IX_family_timeline_events_family_tree_id_member_id",
                table: "family_timeline_events",
                columns: new[] { "family_tree_id", "member_id" });

            migrationBuilder.CreateIndex(
                name: "IX_family_timeline_events_member_id",
                table: "family_timeline_events",
                column: "member_id");

            migrationBuilder.CreateIndex(
                name: "IX_family_timeline_events_related_member_id",
                table: "family_timeline_events",
                column: "related_member_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "family_timeline_events");
        }
    }
}
