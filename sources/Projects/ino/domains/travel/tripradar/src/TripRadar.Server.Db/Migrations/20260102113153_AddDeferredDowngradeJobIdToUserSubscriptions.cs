using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TripRadar.Server.Db.Migrations
{
    /// <inheritdoc />
    public partial class AddDeferredDowngradeJobIdToUserSubscriptions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_TripQueryHistories_TripVaultId",
                schema: "TripRadar.Server",
                table: "TripQueryHistories");

            migrationBuilder.AddColumn<string>(
                name: "DeferredDowngradeJobId",
                schema: "TripRadar.Server",
                table: "UserSubscriptions",
                type: "varchar(255)",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_TripQueryHistories_TripVaultId_CreatedOn",
                schema: "TripRadar.Server",
                table: "TripQueryHistories",
                columns: new[] { "TripVaultId", "CreatedOn" },
                descending: new[] { false, true });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_TripQueryHistories_TripVaultId_CreatedOn",
                schema: "TripRadar.Server",
                table: "TripQueryHistories");

            migrationBuilder.DropColumn(
                name: "DeferredDowngradeJobId",
                schema: "TripRadar.Server",
                table: "UserSubscriptions");

            migrationBuilder.CreateIndex(
                name: "IX_TripQueryHistories_TripVaultId",
                schema: "TripRadar.Server",
                table: "TripQueryHistories",
                column: "TripVaultId");
        }
    }
}
