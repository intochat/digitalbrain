using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace TripRadar.Server.Db.Migrations
{
    /// <inheritdoc />
    public partial class AddAirlinesLookup : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Airlines",
                schema: "TripRadar.Server",
                columns: table => new
                {
                    AirlineId = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    AirlineCode = table.Column<string>(type: "varchar(32)", nullable: false),
                    AirlineName = table.Column<string>(type: "varchar(255)", nullable: false),
                    SearchAliases = table.Column<string>(type: "text", nullable: true),
                    IsAlliance = table.Column<bool>(type: "boolean", nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Airlines", x => x.AirlineId);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Airlines_AirlineCode",
                schema: "TripRadar.Server",
                table: "Airlines",
                column: "AirlineCode",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Airlines_AirlineName",
                schema: "TripRadar.Server",
                table: "Airlines",
                column: "AirlineName");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "Airlines",
                schema: "TripRadar.Server");
        }
    }
}
