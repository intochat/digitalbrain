using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace TripRadar.Server.Db.Migrations
{
    /// <inheritdoc />
    public partial class AddUserProfilesTable : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Email",
                schema: "TripRadar.Server",
                table: "Users");

            migrationBuilder.DropColumn(
                name: "EmailConfirmationToken",
                schema: "TripRadar.Server",
                table: "Users");

            migrationBuilder.DropColumn(
                name: "EmailConfirmationTokenExpiry",
                schema: "TripRadar.Server",
                table: "Users");

            migrationBuilder.DropColumn(
                name: "FirstName",
                schema: "TripRadar.Server",
                table: "Users");

            migrationBuilder.DropColumn(
                name: "GoogleId",
                schema: "TripRadar.Server",
                table: "Users");

            migrationBuilder.DropColumn(
                name: "IpAddress",
                schema: "TripRadar.Server",
                table: "Users");

            migrationBuilder.DropColumn(
                name: "IsEmailConfirmed",
                schema: "TripRadar.Server",
                table: "Users");

            migrationBuilder.DropColumn(
                name: "LastName",
                schema: "TripRadar.Server",
                table: "Users");

            migrationBuilder.DropColumn(
                name: "Password",
                schema: "TripRadar.Server",
                table: "Users");

            migrationBuilder.DropColumn(
                name: "PasswordResetToken",
                schema: "TripRadar.Server",
                table: "Users");

            migrationBuilder.DropColumn(
                name: "PasswordResetTokenExpiry",
                schema: "TripRadar.Server",
                table: "Users");

            migrationBuilder.DropColumn(
                name: "PhoneNumber",
                schema: "TripRadar.Server",
                table: "Users");

            migrationBuilder.DropColumn(
                name: "ProfilePictureUrl",
                schema: "TripRadar.Server",
                table: "Users");

            migrationBuilder.DropColumn(
                name: "RefreshToken",
                schema: "TripRadar.Server",
                table: "Users");

            migrationBuilder.DropColumn(
                name: "RefreshTokenExpiryTime",
                schema: "TripRadar.Server",
                table: "Users");

            migrationBuilder.DropColumn(
                name: "TimeZone",
                schema: "TripRadar.Server",
                table: "Users");

            migrationBuilder.DropColumn(
                name: "Username",
                schema: "TripRadar.Server",
                table: "Users");

            migrationBuilder.CreateTable(
                name: "UserProfiles",
                schema: "TripRadar.Server",
                columns: table => new
                {
                    UserProfileId = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    UserId = table.Column<long>(type: "bigint", nullable: false),
                    Username = table.Column<string>(type: "varchar(255)", nullable: false),
                    Password = table.Column<string>(type: "varchar(255)", nullable: false),
                    Email = table.Column<string>(type: "varchar(255)", nullable: false),
                    IsEmailConfirmed = table.Column<bool>(type: "boolean", nullable: false),
                    EmailConfirmationToken = table.Column<string>(type: "varchar(255)", nullable: true),
                    EmailConfirmationTokenExpiry = table.Column<DateTime>(type: "TIMESTAMPTZ", nullable: true),
                    PasswordResetToken = table.Column<string>(type: "varchar(255)", nullable: true),
                    PasswordResetTokenExpiry = table.Column<DateTime>(type: "TIMESTAMPTZ", nullable: true),
                    FirstName = table.Column<string>(type: "varchar(255)", nullable: true),
                    LastName = table.Column<string>(type: "varchar(255)", nullable: true),
                    PhoneNumber = table.Column<string>(type: "varchar(255)", nullable: true),
                    IpAddress = table.Column<string>(type: "varchar(255)", nullable: true),
                    RefreshToken = table.Column<string>(type: "varchar(255)", nullable: false),
                    RefreshTokenExpiryTime = table.Column<DateTime>(type: "TIMESTAMPTZ", nullable: false),
                    GoogleId = table.Column<string>(type: "varchar(255)", nullable: true),
                    Timezone = table.Column<string>(type: "VARCHAR(100)", nullable: false),
                    ProfilePictureUrl = table.Column<string>(type: "VARCHAR(500)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_UserProfiles", x => x.UserProfileId);
                    table.ForeignKey(
                        name: "FK_UserProfiles_Users_UserId",
                        column: x => x.UserId,
                        principalSchema: "TripRadar.Server",
                        principalTable: "Users",
                        principalColumn: "UserId",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_UserProfiles_UserId",
                schema: "TripRadar.Server",
                table: "UserProfiles",
                column: "UserId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "UserProfiles",
                schema: "TripRadar.Server");

            migrationBuilder.AddColumn<string>(
                name: "Email",
                schema: "TripRadar.Server",
                table: "Users",
                type: "varchar(255)",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "EmailConfirmationToken",
                schema: "TripRadar.Server",
                table: "Users",
                type: "varchar(255)",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "EmailConfirmationTokenExpiry",
                schema: "TripRadar.Server",
                table: "Users",
                type: "TIMESTAMPTZ",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "FirstName",
                schema: "TripRadar.Server",
                table: "Users",
                type: "varchar(255)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "GoogleId",
                schema: "TripRadar.Server",
                table: "Users",
                type: "varchar(255)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "IpAddress",
                schema: "TripRadar.Server",
                table: "Users",
                type: "varchar(255)",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<bool>(
                name: "IsEmailConfirmed",
                schema: "TripRadar.Server",
                table: "Users",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "LastName",
                schema: "TripRadar.Server",
                table: "Users",
                type: "varchar(255)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Password",
                schema: "TripRadar.Server",
                table: "Users",
                type: "varchar(255)",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "PasswordResetToken",
                schema: "TripRadar.Server",
                table: "Users",
                type: "varchar(255)",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "PasswordResetTokenExpiry",
                schema: "TripRadar.Server",
                table: "Users",
                type: "TIMESTAMPTZ",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "PhoneNumber",
                schema: "TripRadar.Server",
                table: "Users",
                type: "varchar(255)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ProfilePictureUrl",
                schema: "TripRadar.Server",
                table: "Users",
                type: "varchar(500)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "RefreshToken",
                schema: "TripRadar.Server",
                table: "Users",
                type: "varchar(255)",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<DateTime>(
                name: "RefreshTokenExpiryTime",
                schema: "TripRadar.Server",
                table: "Users",
                type: "TIMESTAMPTZ",
                nullable: false,
                defaultValue: new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified));

            migrationBuilder.AddColumn<string>(
                name: "TimeZone",
                schema: "TripRadar.Server",
                table: "Users",
                type: "varchar(50)",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "Username",
                schema: "TripRadar.Server",
                table: "Users",
                type: "varchar(255)",
                nullable: false,
                defaultValue: "");
        }
    }
}
