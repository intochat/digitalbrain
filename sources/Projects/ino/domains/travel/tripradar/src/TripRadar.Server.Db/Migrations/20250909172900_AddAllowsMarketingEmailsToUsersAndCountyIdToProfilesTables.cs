using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TripRadar.Server.Db.Migrations
{
    /// <inheritdoc />
    public partial class AddAllowsMarketingEmailsToUsersAndCountyIdToProfilesTables : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "AllowsMarketingEmails",
                schema: "TripRadar.Server",
                table: "Users",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<int>(
                name: "CountryId",
                schema: "TripRadar.Server",
                table: "UserProfiles",
                type: "integer",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_UserProfiles_CountryId",
                schema: "TripRadar.Server",
                table: "UserProfiles",
                column: "CountryId");

            migrationBuilder.AddForeignKey(
                name: "FK_UserProfiles_Countries_CountryId",
                schema: "TripRadar.Server",
                table: "UserProfiles",
                column: "CountryId",
                principalSchema: "TripRadar.Server",
                principalTable: "Countries",
                principalColumn: "CountryId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_UserProfiles_Countries_CountryId",
                schema: "TripRadar.Server",
                table: "UserProfiles");

            migrationBuilder.DropIndex(
                name: "IX_UserProfiles_CountryId",
                schema: "TripRadar.Server",
                table: "UserProfiles");

            migrationBuilder.DropColumn(
                name: "AllowsMarketingEmails",
                schema: "TripRadar.Server",
                table: "Users");

            migrationBuilder.DropColumn(
                name: "CountryId",
                schema: "TripRadar.Server",
                table: "UserProfiles");
        }
    }
}
