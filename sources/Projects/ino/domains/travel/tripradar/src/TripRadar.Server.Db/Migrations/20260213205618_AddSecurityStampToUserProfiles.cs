using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TripRadar.Server.Db.Migrations
{
    /// <inheritdoc />
    public partial class AddSecurityStampToUserProfiles : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "SecurityStamp",
                schema: "TripRadar.Server",
                table: "UserProfiles",
                type: "varchar(64)",
                nullable: false,
                defaultValueSql: "md5(random()::text || clock_timestamp()::text)");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "SecurityStamp",
                schema: "TripRadar.Server",
                table: "UserProfiles");
        }
    }
}
