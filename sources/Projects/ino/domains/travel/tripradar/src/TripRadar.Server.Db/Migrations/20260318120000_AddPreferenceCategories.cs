using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace TripRadar.Server.Db.Migrations
{
    public partial class AddPreferenceCategories : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "PreferenceCategories",
                schema: "TripRadar.Server",
                columns: table => new
                {
                    PreferenceCategoryId = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    Name = table.Column<string>(type: "varchar(100)", nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false, defaultValue: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PreferenceCategories", x => x.PreferenceCategoryId);
                });

            migrationBuilder.CreateIndex(
                name: "IX_PreferenceCategories_Name",
                schema: "TripRadar.Server",
                table: "PreferenceCategories",
                column: "Name",
                unique: true);

            migrationBuilder.AddColumn<int>(
                name: "PreferenceCategoryId",
                schema: "TripRadar.Server",
                table: "ServiceTypes",
                type: "integer",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_ServiceTypes_PreferenceCategoryId",
                schema: "TripRadar.Server",
                table: "ServiceTypes",
                column: "PreferenceCategoryId");

            migrationBuilder.AddForeignKey(
                name: "FK_ServiceTypes_PreferenceCategories_PreferenceCategoryId",
                schema: "TripRadar.Server",
                table: "ServiceTypes",
                column: "PreferenceCategoryId",
                principalSchema: "TripRadar.Server",
                principalTable: "PreferenceCategories",
                principalColumn: "PreferenceCategoryId",
                onDelete: ReferentialAction.Restrict);
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_ServiceTypes_PreferenceCategories_PreferenceCategoryId",
                schema: "TripRadar.Server",
                table: "ServiceTypes");

            migrationBuilder.DropIndex(
                name: "IX_ServiceTypes_PreferenceCategoryId",
                schema: "TripRadar.Server",
                table: "ServiceTypes");

            migrationBuilder.DropColumn(
                name: "PreferenceCategoryId",
                schema: "TripRadar.Server",
                table: "ServiceTypes");

            migrationBuilder.DropTable(
                name: "PreferenceCategories",
                schema: "TripRadar.Server");
        }
    }
}
