using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace TripRadar.Server.Db.Migrations
{
    /// <inheritdoc />
    public partial class AddTimezonesLookupAndUserProfileTimezoneFk : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Timezones",
                schema: "TripRadar.Server",
                columns: table => new
                {
                    TimezoneId = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    TimezoneCode = table.Column<string>(type: "varchar(100)", nullable: false),
                    TimezoneName = table.Column<string>(type: "varchar(100)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Timezones", x => x.TimezoneId);
                });

            migrationBuilder.Sql("""
                                 INSERT INTO "TripRadar.Server"."Timezones" ("TimezoneId", "TimezoneCode", "TimezoneName")
                                 VALUES (1, 'UTC', 'UTC')
                                 ON CONFLICT ("TimezoneId") DO UPDATE
                                 SET "TimezoneCode" = EXCLUDED."TimezoneCode",
                                     "TimezoneName" = EXCLUDED."TimezoneName";
                                 """);

            migrationBuilder.Sql("""
                                 SELECT setval(
                                     pg_get_serial_sequence('"TripRadar.Server"."Timezones"', 'TimezoneId'),
                                     COALESCE((SELECT MAX("TimezoneId") FROM "TripRadar.Server"."Timezones"), 1),
                                     true
                                 );
                                 """);

            migrationBuilder.Sql("""
                                 UPDATE "TripRadar.Server"."UserProfiles"
                                 SET "Timezone" = BTRIM("Timezone")
                                 WHERE "Timezone" <> BTRIM("Timezone");
                                 """);

            migrationBuilder.Sql("""
                                 UPDATE "TripRadar.Server"."UserProfiles"
                                 SET "Timezone" = 'UTC'
                                 WHERE "Timezone" IS NULL OR BTRIM("Timezone") = '';
                                 """);

            migrationBuilder.Sql("""
                                 INSERT INTO "TripRadar.Server"."Timezones" ("TimezoneCode", "TimezoneName")
                                 SELECT DISTINCT user_profiles."Timezone", user_profiles."Timezone"
                                 FROM "TripRadar.Server"."UserProfiles" user_profiles
                                 WHERE user_profiles."Timezone" IS NOT NULL
                                   AND BTRIM(user_profiles."Timezone") <> ''
                                   AND user_profiles."Timezone" <> 'UTC'
                                   AND NOT EXISTS (
                                       SELECT 1
                                       FROM "TripRadar.Server"."Timezones" t
                                       WHERE t."TimezoneCode" = user_profiles."Timezone"
                                   );
                                 """);

            migrationBuilder.AddColumn<int>(
                name: "TimezoneId",
                schema: "TripRadar.Server",
                table: "UserProfiles",
                type: "integer",
                nullable: false,
                defaultValue: 1);

            migrationBuilder.CreateIndex(
                name: "IX_Timezones_TimezoneCode",
                schema: "TripRadar.Server",
                table: "Timezones",
                column: "TimezoneCode",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Timezones_TimezoneName",
                schema: "TripRadar.Server",
                table: "Timezones",
                column: "TimezoneName");

            migrationBuilder.Sql("""
                                 UPDATE "TripRadar.Server"."UserProfiles" up
                                 SET "TimezoneId" = t."TimezoneId"
                                 FROM "TripRadar.Server"."Timezones" t
                                 WHERE t."TimezoneCode" = up."Timezone";
                                 """);

            migrationBuilder.DropColumn(
                name: "Timezone",
                schema: "TripRadar.Server",
                table: "UserProfiles");

            migrationBuilder.CreateIndex(
                name: "IX_UserProfiles_TimezoneId",
                schema: "TripRadar.Server",
                table: "UserProfiles",
                column: "TimezoneId");

            migrationBuilder.AddForeignKey(
                name: "FK_UserProfiles_Timezones_TimezoneId",
                schema: "TripRadar.Server",
                table: "UserProfiles",
                column: "TimezoneId",
                principalSchema: "TripRadar.Server",
                principalTable: "Timezones",
                principalColumn: "TimezoneId",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_UserProfiles_Timezones_TimezoneId",
                schema: "TripRadar.Server",
                table: "UserProfiles");

            migrationBuilder.DropIndex(
                name: "IX_UserProfiles_TimezoneId",
                schema: "TripRadar.Server",
                table: "UserProfiles");

            migrationBuilder.AddColumn<string>(
                name: "Timezone",
                schema: "TripRadar.Server",
                table: "UserProfiles",
                type: "varchar(100)",
                nullable: false,
                defaultValue: "UTC");

            migrationBuilder.Sql("""
                                 UPDATE "TripRadar.Server"."UserProfiles" up
                                 SET "Timezone" = COALESCE(t."TimezoneCode", 'UTC')
                                 FROM "TripRadar.Server"."Timezones" t
                                 WHERE t."TimezoneId" = up."TimezoneId";
                                 """);

            migrationBuilder.DropColumn(
                name: "TimezoneId",
                schema: "TripRadar.Server",
                table: "UserProfiles");

            migrationBuilder.DropTable(
                name: "Timezones",
                schema: "TripRadar.Server");
        }
    }
}
