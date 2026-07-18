using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TripRadar.Server.Db.Migrations
{
    /// <inheritdoc />
    public partial class AddTripVaultIdToScheduledExecutions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<long>(
                name: "TripVaultId",
                schema: "TripRadar.Server",
                table: "ScheduledExecutions",
                type: "bigint",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_ScheduledExecutions_TripVaultId",
                schema: "TripRadar.Server",
                table: "ScheduledExecutions",
                column: "TripVaultId");

            migrationBuilder.AddForeignKey(
                name: "FK_ScheduledExecutions_TripVaults_TripVaultId",
                schema: "TripRadar.Server",
                table: "ScheduledExecutions",
                column: "TripVaultId",
                principalSchema: "TripRadar.Server",
                principalTable: "TripVaults",
                principalColumn: "TripVaultId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_ScheduledExecutions_TripVaults_TripVaultId",
                schema: "TripRadar.Server",
                table: "ScheduledExecutions");

            migrationBuilder.DropIndex(
                name: "IX_ScheduledExecutions_TripVaultId",
                schema: "TripRadar.Server",
                table: "ScheduledExecutions");

            migrationBuilder.DropColumn(
                name: "TripVaultId",
                schema: "TripRadar.Server",
                table: "ScheduledExecutions");
        }
    }
}
