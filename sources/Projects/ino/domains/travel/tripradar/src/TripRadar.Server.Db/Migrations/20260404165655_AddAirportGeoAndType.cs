using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TripRadar.Server.Db.Migrations
{
    /// <inheritdoc />
    public partial class AddAirportGeoAndType : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "AirportType",
                schema: "TripRadar.Server",
                table: "Airports",
                type: "varchar(50)",
                nullable: true);

            migrationBuilder.AddColumn<double>(
                name: "Latitude",
                schema: "TripRadar.Server",
                table: "Airports",
                type: "double precision",
                nullable: true);

            migrationBuilder.AddColumn<double>(
                name: "Longitude",
                schema: "TripRadar.Server",
                table: "Airports",
                type: "double precision",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "AirportType",
                schema: "TripRadar.Server",
                table: "Airports");

            migrationBuilder.DropColumn(
                name: "Latitude",
                schema: "TripRadar.Server",
                table: "Airports");

            migrationBuilder.DropColumn(
                name: "Longitude",
                schema: "TripRadar.Server",
                table: "Airports");
        }
    }
}
