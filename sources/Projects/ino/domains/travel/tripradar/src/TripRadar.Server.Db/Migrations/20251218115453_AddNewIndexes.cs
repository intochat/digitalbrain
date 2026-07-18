using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TripRadar.Server.Db.Migrations
{
    /// <inheritdoc />
    public partial class AddNewIndexes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "ProcessingId",
                schema: "TripRadar.Server",
                table: "OverageBillingRecords",
                type: "varchar(255)",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "ProcessingStartedAt",
                schema: "TripRadar.Server",
                table: "OverageBillingRecords",
                type: "TIMESTAMPTZ",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ProcessingId",
                schema: "TripRadar.Server",
                table: "OverageBillingRecords");

            migrationBuilder.DropColumn(
                name: "ProcessingStartedAt",
                schema: "TripRadar.Server",
                table: "OverageBillingRecords");
        }
    }
}
