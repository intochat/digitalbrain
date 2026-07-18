using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TripRadar.Server.Db.Migrations
{
    public partial class DropPreferenceTypesDisplayNameAndDescription : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "DisplayName",
                schema: "TripRadar.Server",
                table: "PreferenceTypes");

            migrationBuilder.DropColumn(
                name: "Description",
                schema: "TripRadar.Server",
                table: "PreferenceTypes");
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "DisplayName",
                schema: "TripRadar.Server",
                table: "PreferenceTypes",
                type: "character varying(200)",
                maxLength: 200,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "Description",
                schema: "TripRadar.Server",
                table: "PreferenceTypes",
                type: "character varying(500)",
                maxLength: 500,
                nullable: true);
        }
    }
}