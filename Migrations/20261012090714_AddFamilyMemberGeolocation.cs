using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MyLife.Migrations
{
    /// <inheritdoc />
    public partial class AddFamilyMemberGeolocation : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "geocoded_address",
                table: "family_members",
                type: "character varying(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "geocoded_at",
                table: "family_members",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<double>(
                name: "latitude",
                table: "family_members",
                type: "double precision",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "location_display_name",
                table: "family_members",
                type: "character varying(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<double>(
                name: "longitude",
                table: "family_members",
                type: "double precision",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "geocoded_address",
                table: "family_members");

            migrationBuilder.DropColumn(
                name: "geocoded_at",
                table: "family_members");

            migrationBuilder.DropColumn(
                name: "latitude",
                table: "family_members");

            migrationBuilder.DropColumn(
                name: "location_display_name",
                table: "family_members");

            migrationBuilder.DropColumn(
                name: "longitude",
                table: "family_members");
        }
    }
}
