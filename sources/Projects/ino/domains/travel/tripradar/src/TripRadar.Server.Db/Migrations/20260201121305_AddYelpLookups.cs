using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace TripRadar.Server.Db.Migrations
{
    /// <inheritdoc />
    public partial class AddYelpLookups : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "YelpDomains",
                schema: "TripRadar.Server",
                columns: table => new
                {
                    YelpDomainId = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    Domain = table.Column<string>(type: "varchar(100)", nullable: false),
                    Locale = table.Column<string>(type: "varchar(100)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_YelpDomains", x => x.YelpDomainId);
                });

            migrationBuilder.CreateTable(
                name: "YelpReviewLanguages",
                schema: "TripRadar.Server",
                columns: table => new
                {
                    YelpReviewLanguageId = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    LanguageCode = table.Column<string>(type: "varchar(10)", nullable: false),
                    LanguageName = table.Column<string>(type: "varchar(100)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_YelpReviewLanguages", x => x.YelpReviewLanguageId);
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "YelpDomains",
                schema: "TripRadar.Server");

            migrationBuilder.DropTable(
                name: "YelpReviewLanguages",
                schema: "TripRadar.Server");
        }
    }
}
