using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace TripRadar.Server.Db.Migrations
{
    /// <inheritdoc />
    public partial class AddPromocodesTable : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<long>(
                name: "PromoCodeId",
                schema: "TripRadar.Server",
                table: "Users",
                type: "bigint",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "DiscountTypes",
                schema: "TripRadar.Server",
                columns: table => new
                {
                    DiscountTypeId = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    Name = table.Column<string>(type: "varchar(50)", nullable: false),
                    Description = table.Column<string>(type: "varchar(200)", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "TIMESTAMPTZ", nullable: false),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DiscountTypes", x => x.DiscountTypeId);
                });

            migrationBuilder.CreateTable(
                name: "PromoCodes",
                schema: "TripRadar.Server",
                columns: table => new
                {
                    PromoCodeId = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    Code = table.Column<string>(type: "varchar(50)", nullable: false),
                    Description = table.Column<string>(type: "varchar(500)", nullable: true),
                    DiscountTypeId = table.Column<int>(type: "integer", nullable: false),
                    DiscountValue = table.Column<decimal>(type: "numeric(18,2)", nullable: false),
                    MaxUsageCount = table.Column<int>(type: "integer", nullable: true),
                    CurrentUsageCount = table.Column<int>(type: "integer", nullable: false),
                    MaxUsagePerUser = table.Column<int>(type: "integer", nullable: false),
                    StartDate = table.Column<DateTimeOffset>(type: "TIMESTAMPTZ", nullable: false),
                    EndDate = table.Column<DateTimeOffset>(type: "TIMESTAMPTZ", nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "TIMESTAMPTZ", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "TIMESTAMPTZ", nullable: true),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PromoCodes", x => x.PromoCodeId);
                });

            migrationBuilder.CreateTable(
                name: "PromoCodeUsages",
                schema: "TripRadar.Server",
                columns: table => new
                {
                    PromoCodeUsageId = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    PromoCodeId = table.Column<long>(type: "bigint", nullable: false),
                    UserId = table.Column<long>(type: "bigint", nullable: false),
                    UsedAt = table.Column<DateTime>(type: "TIMESTAMPTZ", nullable: false),
                    DiscountApplied = table.Column<decimal>(type: "numeric(18,2)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PromoCodeUsages", x => x.PromoCodeUsageId);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Users_PromoCodeId",
                schema: "TripRadar.Server",
                table: "Users",
                column: "PromoCodeId");

            migrationBuilder.AddForeignKey(
                name: "FK_Users_PromoCodes_PromoCodeId",
                schema: "TripRadar.Server",
                table: "Users",
                column: "PromoCodeId",
                principalSchema: "TripRadar.Server",
                principalTable: "PromoCodes",
                principalColumn: "PromoCodeId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Users_PromoCodes_PromoCodeId",
                schema: "TripRadar.Server",
                table: "Users");

            migrationBuilder.DropTable(
                name: "DiscountTypes",
                schema: "TripRadar.Server");

            migrationBuilder.DropTable(
                name: "PromoCodes",
                schema: "TripRadar.Server");

            migrationBuilder.DropTable(
                name: "PromoCodeUsages",
                schema: "TripRadar.Server");

            migrationBuilder.DropIndex(
                name: "IX_Users_PromoCodeId",
                schema: "TripRadar.Server",
                table: "Users");

            migrationBuilder.DropColumn(
                name: "PromoCodeId",
                schema: "TripRadar.Server",
                table: "Users");
        }
    }
}
