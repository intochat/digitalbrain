using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TripRadar.Server.Db.Migrations
{
    /// <inheritdoc />
    public partial class AddAccountLockoutFields : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "AccessFailedCount",
                schema: "TripRadar.Server",
                table: "UserProfiles",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<bool>(
                name: "LockoutEnabled",
                schema: "TripRadar.Server",
                table: "UserProfiles",
                type: "boolean",
                nullable: false,
                defaultValue: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "LockoutEnd",
                schema: "TripRadar.Server",
                table: "UserProfiles",
                type: "TIMESTAMPTZ",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "AccessFailedCount",
                schema: "TripRadar.Server",
                table: "UserProfiles");

            migrationBuilder.DropColumn(
                name: "LockoutEnabled",
                schema: "TripRadar.Server",
                table: "UserProfiles");

            migrationBuilder.DropColumn(
                name: "LockoutEnd",
                schema: "TripRadar.Server",
                table: "UserProfiles");
        }
    }
}
