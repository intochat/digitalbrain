using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace TripRadar.Server.Db.Migrations
{
    /// <inheritdoc />
    public partial class AddTripVaultTables : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "TripVaults",
                schema: "TripRadar.Server",
                columns: table => new
                {
                    TripVaultId = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    UniqueId = table.Column<Guid>(type: "uuid", nullable: false),
                    OwnerId = table.Column<long>(type: "bigint", nullable: false),
                    Name = table.Column<string>(type: "varchar(255)", nullable: false),
                    Description = table.Column<string>(type: "varchar(500)", nullable: true),
                    StartDate = table.Column<DateTime>(type: "TIMESTAMPTZ", nullable: true),
                    EndDate = table.Column<DateTime>(type: "TIMESTAMPTZ", nullable: true),
                    CreatedOn = table.Column<DateTime>(type: "TIMESTAMPTZ", nullable: false),
                    UpdatedOn = table.Column<DateTime>(type: "TIMESTAMPTZ", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TripVaults", x => x.TripVaultId);
                    table.ForeignKey(
                        name: "FK_TripVaults_Users_OwnerId",
                        column: x => x.OwnerId,
                        principalSchema: "TripRadar.Server",
                        principalTable: "Users",
                        principalColumn: "UserId",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "TripQueryHistories",
                schema: "TripRadar.Server",
                columns: table => new
                {
                    TripQueryHistoryId = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    UniqueId = table.Column<Guid>(type: "uuid", nullable: false),
                    TripVaultId = table.Column<long>(type: "bigint", nullable: false),
                    ServiceTypeId = table.Column<int>(type: "integer", nullable: false),
                    QueryParametersJson = table.Column<string>(type: "jsonb", nullable: false),
                    StartDateTime = table.Column<DateTime>(type: "TIMESTAMPTZ", nullable: true),
                    EndDateTime = table.Column<DateTime>(type: "TIMESTAMPTZ", nullable: true),
                    ResultSummary = table.Column<string>(type: "text", nullable: true),
                    CreatedOn = table.Column<DateTime>(type: "TIMESTAMPTZ", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TripQueryHistories", x => x.TripQueryHistoryId);
                    table.ForeignKey(
                        name: "FK_TripQueryHistories_ServiceTypes_ServiceTypeId",
                        column: x => x.ServiceTypeId,
                        principalSchema: "TripRadar.Server",
                        principalTable: "ServiceTypes",
                        principalColumn: "ServiceTypeId",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_TripQueryHistories_TripVaults_TripVaultId",
                        column: x => x.TripVaultId,
                        principalSchema: "TripRadar.Server",
                        principalTable: "TripVaults",
                        principalColumn: "TripVaultId",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_TripQueryHistories_ServiceTypeId",
                schema: "TripRadar.Server",
                table: "TripQueryHistories",
                column: "ServiceTypeId");

            migrationBuilder.CreateIndex(
                name: "IX_TripQueryHistories_TripVaultId",
                schema: "TripRadar.Server",
                table: "TripQueryHistories",
                column: "TripVaultId");

            migrationBuilder.CreateIndex(
                name: "IX_TripVaults_OwnerId",
                schema: "TripRadar.Server",
                table: "TripVaults",
                column: "OwnerId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "TripQueryHistories",
                schema: "TripRadar.Server");

            migrationBuilder.DropTable(
                name: "TripVaults",
                schema: "TripRadar.Server");
        }
    }
}
