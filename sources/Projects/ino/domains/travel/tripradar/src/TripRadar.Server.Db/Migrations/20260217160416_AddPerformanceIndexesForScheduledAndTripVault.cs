using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TripRadar.Server.Db.Migrations
{
    /// <inheritdoc />
    public partial class AddPerformanceIndexesForScheduledAndTripVault : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_TripVaults_OwnerId",
                schema: "TripRadar.Server",
                table: "TripVaults");

            migrationBuilder.DropIndex(
                name: "IX_ScheduledLocalPlacesQueries_ScheduledExecutionId",
                schema: "TripRadar.Server",
                table: "ScheduledLocalPlacesQueries");

            migrationBuilder.DropIndex(
                name: "IX_ScheduledHotelQueries_ScheduledExecutionId",
                schema: "TripRadar.Server",
                table: "ScheduledHotelQueries");

            migrationBuilder.DropIndex(
                name: "IX_ScheduledFlightQueries_ScheduledExecutionId",
                schema: "TripRadar.Server",
                table: "ScheduledFlightQueries");

            migrationBuilder.DropIndex(
                name: "IX_ScheduledExecutions_UserId",
                schema: "TripRadar.Server",
                table: "ScheduledExecutions");

            migrationBuilder.DropIndex(
                name: "IX_ScheduledEventQueries_ScheduledExecutionId",
                schema: "TripRadar.Server",
                table: "ScheduledEventQueries");

            migrationBuilder.CreateIndex(
                name: "IX_TripVaults_OwnerId_CreatedOn",
                schema: "TripRadar.Server",
                table: "TripVaults",
                columns: new[] { "OwnerId", "CreatedOn" },
                descending: new[] { false, true });

            migrationBuilder.CreateIndex(
                name: "IX_TripVaults_UniqueId",
                schema: "TripRadar.Server",
                table: "TripVaults",
                column: "UniqueId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ScheduledLocalPlacesQueries_ScheduledExecutionId_CreatedOn",
                schema: "TripRadar.Server",
                table: "ScheduledLocalPlacesQueries",
                columns: new[] { "ScheduledExecutionId", "CreatedOn" },
                descending: new[] { false, true });

            migrationBuilder.CreateIndex(
                name: "IX_ScheduledLocalPlacesQueries_UniqueId",
                schema: "TripRadar.Server",
                table: "ScheduledLocalPlacesQueries",
                column: "UniqueId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ScheduledHotelQueries_ScheduledExecutionId_CreatedOn",
                schema: "TripRadar.Server",
                table: "ScheduledHotelQueries",
                columns: new[] { "ScheduledExecutionId", "CreatedOn" },
                descending: new[] { false, true });

            migrationBuilder.CreateIndex(
                name: "IX_ScheduledHotelQueries_UniqueId",
                schema: "TripRadar.Server",
                table: "ScheduledHotelQueries",
                column: "UniqueId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ScheduledFlightQueries_ScheduledExecutionId_CreatedOn",
                schema: "TripRadar.Server",
                table: "ScheduledFlightQueries",
                columns: new[] { "ScheduledExecutionId", "CreatedOn" },
                descending: new[] { false, true });

            migrationBuilder.CreateIndex(
                name: "IX_ScheduledFlightQueries_UniqueId",
                schema: "TripRadar.Server",
                table: "ScheduledFlightQueries",
                column: "UniqueId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ScheduledExecutions_IsActive_NextExecutionTime",
                schema: "TripRadar.Server",
                table: "ScheduledExecutions",
                columns: new[] { "IsActive", "NextExecutionTime" });

            migrationBuilder.CreateIndex(
                name: "IX_ScheduledExecutions_UniqueId",
                schema: "TripRadar.Server",
                table: "ScheduledExecutions",
                column: "UniqueId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ScheduledExecutions_UserId_CreatedOn",
                schema: "TripRadar.Server",
                table: "ScheduledExecutions",
                columns: new[] { "UserId", "CreatedOn" },
                descending: new[] { false, true });

            migrationBuilder.CreateIndex(
                name: "IX_ScheduledEventQueries_ScheduledExecutionId_CreatedOn",
                schema: "TripRadar.Server",
                table: "ScheduledEventQueries",
                columns: new[] { "ScheduledExecutionId", "CreatedOn" },
                descending: new[] { false, true });

            migrationBuilder.CreateIndex(
                name: "IX_ScheduledEventQueries_UniqueId",
                schema: "TripRadar.Server",
                table: "ScheduledEventQueries",
                column: "UniqueId",
                unique: true);

            migrationBuilder.Sql(
                """
                CREATE INDEX IF NOT EXISTS "IX_TripVaults_OwnerId_LowerName"
                ON "TripRadar.Server"."TripVaults" ("OwnerId", lower("Name"));
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_TripVaults_OwnerId_CreatedOn",
                schema: "TripRadar.Server",
                table: "TripVaults");

            migrationBuilder.DropIndex(
                name: "IX_TripVaults_UniqueId",
                schema: "TripRadar.Server",
                table: "TripVaults");

            migrationBuilder.DropIndex(
                name: "IX_ScheduledLocalPlacesQueries_ScheduledExecutionId_CreatedOn",
                schema: "TripRadar.Server",
                table: "ScheduledLocalPlacesQueries");

            migrationBuilder.DropIndex(
                name: "IX_ScheduledLocalPlacesQueries_UniqueId",
                schema: "TripRadar.Server",
                table: "ScheduledLocalPlacesQueries");

            migrationBuilder.DropIndex(
                name: "IX_ScheduledHotelQueries_ScheduledExecutionId_CreatedOn",
                schema: "TripRadar.Server",
                table: "ScheduledHotelQueries");

            migrationBuilder.DropIndex(
                name: "IX_ScheduledHotelQueries_UniqueId",
                schema: "TripRadar.Server",
                table: "ScheduledHotelQueries");

            migrationBuilder.DropIndex(
                name: "IX_ScheduledFlightQueries_ScheduledExecutionId_CreatedOn",
                schema: "TripRadar.Server",
                table: "ScheduledFlightQueries");

            migrationBuilder.DropIndex(
                name: "IX_ScheduledFlightQueries_UniqueId",
                schema: "TripRadar.Server",
                table: "ScheduledFlightQueries");

            migrationBuilder.DropIndex(
                name: "IX_ScheduledExecutions_IsActive_NextExecutionTime",
                schema: "TripRadar.Server",
                table: "ScheduledExecutions");

            migrationBuilder.DropIndex(
                name: "IX_ScheduledExecutions_UniqueId",
                schema: "TripRadar.Server",
                table: "ScheduledExecutions");

            migrationBuilder.DropIndex(
                name: "IX_ScheduledExecutions_UserId_CreatedOn",
                schema: "TripRadar.Server",
                table: "ScheduledExecutions");

            migrationBuilder.DropIndex(
                name: "IX_ScheduledEventQueries_ScheduledExecutionId_CreatedOn",
                schema: "TripRadar.Server",
                table: "ScheduledEventQueries");

            migrationBuilder.DropIndex(
                name: "IX_ScheduledEventQueries_UniqueId",
                schema: "TripRadar.Server",
                table: "ScheduledEventQueries");

            migrationBuilder.Sql(
                """
                DROP INDEX IF EXISTS "TripRadar.Server"."IX_TripVaults_OwnerId_LowerName";
                """);

            migrationBuilder.CreateIndex(
                name: "IX_TripVaults_OwnerId",
                schema: "TripRadar.Server",
                table: "TripVaults",
                column: "OwnerId");

            migrationBuilder.CreateIndex(
                name: "IX_ScheduledLocalPlacesQueries_ScheduledExecutionId",
                schema: "TripRadar.Server",
                table: "ScheduledLocalPlacesQueries",
                column: "ScheduledExecutionId");

            migrationBuilder.CreateIndex(
                name: "IX_ScheduledHotelQueries_ScheduledExecutionId",
                schema: "TripRadar.Server",
                table: "ScheduledHotelQueries",
                column: "ScheduledExecutionId");

            migrationBuilder.CreateIndex(
                name: "IX_ScheduledFlightQueries_ScheduledExecutionId",
                schema: "TripRadar.Server",
                table: "ScheduledFlightQueries",
                column: "ScheduledExecutionId");

            migrationBuilder.CreateIndex(
                name: "IX_ScheduledExecutions_UserId",
                schema: "TripRadar.Server",
                table: "ScheduledExecutions",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_ScheduledEventQueries_ScheduledExecutionId",
                schema: "TripRadar.Server",
                table: "ScheduledEventQueries",
                column: "ScheduledExecutionId");
        }
    }
}
