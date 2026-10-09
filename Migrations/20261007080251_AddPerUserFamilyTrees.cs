using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace MyLife.Migrations
{
    /// <inheritdoc />
    public partial class AddPerUserFamilyTrees : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Ownership cannot be inferred. Refuse unexpected legacy member data.
            migrationBuilder.Sql("DO $ownership$ BEGIN IF EXISTS (SELECT 1 FROM family_members) THEN RAISE EXCEPTION 'family_members must be empty before AddPerUserFamilyTrees'; END IF; END $ownership$;");
            migrationBuilder.CreateTable(
                name: "family_trees",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    owner_user_id = table.Column<int>(type: "integer", nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_family_trees", x => x.id);
                    table.ForeignKey(
                        name: "FK_family_trees_users_owner_user_id",
                        column: x => x.owner_user_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.AddColumn<long>(
                name: "family_tree_id",
                table: "family_members",
                type: "bigint",
                nullable: false);

            migrationBuilder.CreateIndex(
                name: "IX_family_members_family_tree_id",
                table: "family_members",
                column: "family_tree_id");

            migrationBuilder.CreateIndex(
                name: "IX_family_members_family_tree_id_generation",
                table: "family_members",
                columns: new[] { "family_tree_id", "generation" });

            migrationBuilder.CreateIndex(
                name: "IX_family_trees_owner_user_id",
                table: "family_trees",
                column: "owner_user_id",
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "FK_family_members_family_trees_family_tree_id",
                table: "family_members",
                column: "family_tree_id",
                principalTable: "family_trees",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_family_members_family_trees_family_tree_id",
                table: "family_members");

            migrationBuilder.DropIndex(
                name: "IX_family_members_family_tree_id",
                table: "family_members");

            migrationBuilder.DropIndex(
                name: "IX_family_members_family_tree_id_generation",
                table: "family_members");

            migrationBuilder.DropColumn(
                name: "family_tree_id",
                table: "family_members");

            migrationBuilder.DropTable(
                name: "family_trees");

        }
    }
}
