using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TripRadar.Server.Db.Migrations
{
    /// <inheritdoc />
    public partial class AddUserProfileHashes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "EmailHash",
                schema: "TripRadar.Server",
                table: "UserProfiles",
                type: "varchar(64)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "UsernameHash",
                schema: "TripRadar.Server",
                table: "UserProfiles",
                type: "varchar(64)",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_UserProfiles_EmailHash",
                schema: "TripRadar.Server",
                table: "UserProfiles",
                column: "EmailHash",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_UserProfiles_UsernameHash",
                schema: "TripRadar.Server",
                table: "UserProfiles",
                column: "UsernameHash",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_UserProfiles_EmailHash",
                schema: "TripRadar.Server",
                table: "UserProfiles");

            migrationBuilder.DropIndex(
                name: "IX_UserProfiles_UsernameHash",
                schema: "TripRadar.Server",
                table: "UserProfiles");

            migrationBuilder.DropColumn(
                name: "EmailHash",
                schema: "TripRadar.Server",
                table: "UserProfiles");

            migrationBuilder.DropColumn(
                name: "UsernameHash",
                schema: "TripRadar.Server",
                table: "UserProfiles");
        }
    }
}
