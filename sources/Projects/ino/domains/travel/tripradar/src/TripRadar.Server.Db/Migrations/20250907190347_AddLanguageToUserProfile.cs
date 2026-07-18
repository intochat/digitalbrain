using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TripRadar.Server.Db.Migrations
{
    /// <inheritdoc />
    public partial class AddLanguageToUserProfile : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "LanguageId",
                schema: "TripRadar.Server",
                table: "UserProfiles",
                type: "integer",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_UserProfiles_LanguageId",
                schema: "TripRadar.Server",
                table: "UserProfiles",
                column: "LanguageId");

            migrationBuilder.AddForeignKey(
                name: "FK_UserProfiles_Languages_LanguageId",
                schema: "TripRadar.Server",
                table: "UserProfiles",
                column: "LanguageId",
                principalSchema: "TripRadar.Server",
                principalTable: "Languages",
                principalColumn: "LanguageId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_UserProfiles_Languages_LanguageId",
                schema: "TripRadar.Server",
                table: "UserProfiles");

            migrationBuilder.DropIndex(
                name: "IX_UserProfiles_LanguageId",
                schema: "TripRadar.Server",
                table: "UserProfiles");

            migrationBuilder.DropColumn(
                name: "LanguageId",
                schema: "TripRadar.Server",
                table: "UserProfiles");
        }
    }
}
