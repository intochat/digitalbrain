using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace TripRadar.Server.Db.Migrations
{
    /// <inheritdoc />
    public partial class AddUsageEvents : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "UsageEventSources",
                schema: "TripRadar.Server",
                columns: table => new
                {
                    UsageEventSourceId = table.Column<int>(type: "integer", nullable: false),
                    Name = table.Column<string>(type: "varchar(50)", nullable: false),
                    Description = table.Column<string>(type: "varchar(200)", nullable: true),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_UsageEventSources", x => x.UsageEventSourceId);
                });

            migrationBuilder.CreateTable(
                name: "UsageEvents",
                schema: "TripRadar.Server",
                columns: table => new
                {
                    UsageEventId = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    UniqueId = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<long>(type: "bigint", nullable: false),
                    ServiceTypeId = table.Column<int>(type: "integer", nullable: false),
                    TripVaultId = table.Column<long>(type: "bigint", nullable: true),
                    UsageEventSourceId = table.Column<int>(type: "integer", nullable: false),
                    TokensConsumed = table.Column<decimal>(type: "numeric(10,2)", nullable: false),
                    OccurredAt = table.Column<DateTime>(type: "TIMESTAMPTZ", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "TIMESTAMPTZ", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_UsageEvents", x => x.UsageEventId);
                    table.ForeignKey(
                        name: "FK_UsageEvents_ServiceTypes_ServiceTypeId",
                        column: x => x.ServiceTypeId,
                        principalSchema: "TripRadar.Server",
                        principalTable: "ServiceTypes",
                        principalColumn: "ServiceTypeId",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_UsageEvents_TripVaults_TripVaultId",
                        column: x => x.TripVaultId,
                        principalSchema: "TripRadar.Server",
                        principalTable: "TripVaults",
                        principalColumn: "TripVaultId");
                    table.ForeignKey(
                        name: "FK_UsageEvents_UsageEventSources_UsageEventSourceId",
                        column: x => x.UsageEventSourceId,
                        principalSchema: "TripRadar.Server",
                        principalTable: "UsageEventSources",
                        principalColumn: "UsageEventSourceId",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_UsageEvents_Users_UserId",
                        column: x => x.UserId,
                        principalSchema: "TripRadar.Server",
                        principalTable: "Users",
                        principalColumn: "UserId",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_UsageEvents_ServiceTypeId",
                schema: "TripRadar.Server",
                table: "UsageEvents",
                column: "ServiceTypeId");

            migrationBuilder.CreateIndex(
                name: "IX_UsageEvents_TripVaultId",
                schema: "TripRadar.Server",
                table: "UsageEvents",
                column: "TripVaultId");

            migrationBuilder.CreateIndex(
                name: "IX_UsageEvents_UniqueId",
                schema: "TripRadar.Server",
                table: "UsageEvents",
                column: "UniqueId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_UsageEvents_UsageEventSourceId",
                schema: "TripRadar.Server",
                table: "UsageEvents",
                column: "UsageEventSourceId");

            migrationBuilder.CreateIndex(
                name: "IX_UsageEvents_UserId_OccurredAt",
                schema: "TripRadar.Server",
                table: "UsageEvents",
                columns: new[] { "UserId", "OccurredAt" },
                descending: new[] { false, true });

            migrationBuilder.CreateIndex(
                name: "IX_UsageEvents_UserId_ServiceTypeId_OccurredAt",
                schema: "TripRadar.Server",
                table: "UsageEvents",
                columns: new[] { "UserId", "ServiceTypeId", "OccurredAt" },
                descending: new[] { false, false, true });

            migrationBuilder.CreateIndex(
                name: "IX_UsageEvents_UserId_TripVaultId_OccurredAt",
                schema: "TripRadar.Server",
                table: "UsageEvents",
                columns: new[] { "UserId", "TripVaultId", "OccurredAt" },
                descending: new[] { false, false, true });

            migrationBuilder.CreateIndex(
                name: "IX_UsageEvents_UserId_UsageEventSourceId_OccurredAt",
                schema: "TripRadar.Server",
                table: "UsageEvents",
                columns: new[] { "UserId", "UsageEventSourceId", "OccurredAt" },
                descending: new[] { false, false, true });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "UsageEvents",
                schema: "TripRadar.Server");

            migrationBuilder.DropTable(
                name: "UsageEventSources",
                schema: "TripRadar.Server");
        }
    }
}
