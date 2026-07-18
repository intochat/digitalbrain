using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TripRadar.Server.Db.Migrations
{
    /// <inheritdoc />
    public partial class AddAirportSearchAliases : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "SearchAliases",
                schema: "TripRadar.Server",
                table: "Airlines");

            migrationBuilder.AddColumn<string>(
                name: "SearchAliases",
                schema: "TripRadar.Server",
                table: "Airports",
                type: "text",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "SearchAliases",
                schema: "TripRadar.Server",
                table: "Airports");

            migrationBuilder.AddColumn<string>(
                name: "SearchAliases",
                schema: "TripRadar.Server",
                table: "Airlines",
                type: "text",
                nullable: true);
        }
    }
}
