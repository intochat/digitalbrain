using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TripRadar.Server.Db.Migrations
{
    /// <inheritdoc />
    public partial class AddPriceStripeIdHash : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "StripeIdHash",
                schema: "TripRadar.Server",
                table: "Prices",
                type: "varchar(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_Prices_StripeIdHash",
                schema: "TripRadar.Server",
                table: "Prices",
                column: "StripeIdHash");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Prices_StripeIdHash",
                schema: "TripRadar.Server",
                table: "Prices");

            migrationBuilder.DropColumn(
                name: "StripeIdHash",
                schema: "TripRadar.Server",
                table: "Prices");
        }
    }
}
