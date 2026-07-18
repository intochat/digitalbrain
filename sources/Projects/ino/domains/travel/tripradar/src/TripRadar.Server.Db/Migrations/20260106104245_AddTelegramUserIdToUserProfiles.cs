using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TripRadar.Server.Db.Migrations
{
    /// <inheritdoc />
    public partial class AddTelegramUserIdToUserProfiles : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<long>(
                name: "TelegramUserId",
                schema: "TripRadar.Server",
                table: "UserProfiles",
                type: "bigint",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_UserProfiles_TelegramUserId",
                schema: "TripRadar.Server",
                table: "UserProfiles",
                column: "TelegramUserId",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_UserProfiles_TelegramUserId",
                schema: "TripRadar.Server",
                table: "UserProfiles");

            migrationBuilder.DropColumn(
                name: "TelegramUserId",
                schema: "TripRadar.Server",
                table: "UserProfiles");
        }
    }
}
