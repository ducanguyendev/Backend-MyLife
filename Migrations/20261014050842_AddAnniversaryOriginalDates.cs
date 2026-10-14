using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MyLife.Migrations
{
    /// <inheritdoc />
    public partial class AddAnniversaryOriginalDates : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "lunar_year",
                table: "family_anniversaries",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<DateOnly>(
                name: "original_solar_date",
                table: "family_anniversaries",
                type: "date",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "lunar_year",
                table: "family_anniversaries");

            migrationBuilder.DropColumn(
                name: "original_solar_date",
                table: "family_anniversaries");
        }
    }
}
