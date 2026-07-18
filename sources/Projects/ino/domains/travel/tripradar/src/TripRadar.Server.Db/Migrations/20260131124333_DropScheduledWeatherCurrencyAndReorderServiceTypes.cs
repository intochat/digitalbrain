using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace TripRadar.Server.Db.Migrations
{
    /// <inheritdoc />
    public partial class DropScheduledWeatherCurrencyAndReorderServiceTypes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_OverageBillingRecords_ServiceTypes_ServiceTypeId",
                schema: "TripRadar.Server",
                table: "OverageBillingRecords");

            migrationBuilder.DropForeignKey(
                name: "FK_PreferenceTypes_ServiceTypes_ServiceTypeId",
                schema: "TripRadar.Server",
                table: "PreferenceTypes");

            migrationBuilder.DropForeignKey(
                name: "FK_ServiceTokenCosts_ServiceTypes_ServiceTypeId",
                schema: "TripRadar.Server",
                table: "ServiceTokenCosts");

            migrationBuilder.DropForeignKey(
                name: "FK_TripQueryHistories_ServiceTypes_ServiceTypeId",
                schema: "TripRadar.Server",
                table: "TripQueryHistories");

            migrationBuilder.Sql(
                """
                DELETE FROM "TripRadar.Server"."ServiceTokenCosts"
                WHERE "ServiceTypeId" IN (
                    SELECT "ServiceTypeId"
                    FROM "TripRadar.Server"."ServiceTypes"
                    WHERE "Name" NOT IN (
                        'Event',
                        'Flight',
                        'Hotel',
                        'LocalPlaces',
                        'Maps',
                        'PlaceReview',
                        'FlightExplore',
                        'TripAdvisorSearch',
                        'TripAdvisorPlace'
                    )
                );

                DELETE FROM "TripRadar.Server"."PreferenceTypes"
                WHERE "ServiceTypeId" IN (
                    SELECT "ServiceTypeId"
                    FROM "TripRadar.Server"."ServiceTypes"
                    WHERE "Name" NOT IN (
                        'Event',
                        'Flight',
                        'Hotel',
                        'LocalPlaces',
                        'Maps',
                        'PlaceReview',
                        'FlightExplore',
                        'TripAdvisorSearch',
                        'TripAdvisorPlace'
                    )
                );

                DELETE FROM "TripRadar.Server"."OverageBillingRecords"
                WHERE "ServiceTypeId" IN (
                    SELECT "ServiceTypeId"
                    FROM "TripRadar.Server"."ServiceTypes"
                    WHERE "Name" NOT IN (
                        'Event',
                        'Flight',
                        'Hotel',
                        'LocalPlaces',
                        'Maps',
                        'PlaceReview',
                        'FlightExplore',
                        'TripAdvisorSearch',
                        'TripAdvisorPlace'
                    )
                );

                DELETE FROM "TripRadar.Server"."TripQueryHistories"
                WHERE "ServiceTypeId" IN (
                    SELECT "ServiceTypeId"
                    FROM "TripRadar.Server"."ServiceTypes"
                    WHERE "Name" NOT IN (
                        'Event',
                        'Flight',
                        'Hotel',
                        'LocalPlaces',
                        'Maps',
                        'PlaceReview',
                        'FlightExplore',
                        'TripAdvisorSearch',
                        'TripAdvisorPlace'
                    )
                );

                DELETE FROM "TripRadar.Server"."ServiceTypes"
                WHERE "Name" NOT IN (
                    'Event',
                    'Flight',
                    'Hotel',
                    'LocalPlaces',
                    'Maps',
                    'PlaceReview',
                    'FlightExplore',
                    'TripAdvisorSearch',
                    'TripAdvisorPlace'
                );

                UPDATE "TripRadar.Server"."ServiceTokenCosts" stc
                SET "ServiceTypeId" = CASE st."Name"
                    WHEN 'Event' THEN 1
                    WHEN 'Flight' THEN 2
                    WHEN 'Hotel' THEN 3
                    WHEN 'LocalPlaces' THEN 4
                    WHEN 'Maps' THEN 5
                    WHEN 'PlaceReview' THEN 6
                    WHEN 'FlightExplore' THEN 7
                    WHEN 'TripAdvisorSearch' THEN 8
                    WHEN 'TripAdvisorPlace' THEN 9
                END
                FROM "TripRadar.Server"."ServiceTypes" st
                WHERE stc."ServiceTypeId" = st."ServiceTypeId";

                UPDATE "TripRadar.Server"."PreferenceTypes" pt
                SET "ServiceTypeId" = CASE st."Name"
                    WHEN 'Event' THEN 1
                    WHEN 'Flight' THEN 2
                    WHEN 'Hotel' THEN 3
                    WHEN 'LocalPlaces' THEN 4
                    WHEN 'Maps' THEN 5
                    WHEN 'PlaceReview' THEN 6
                    WHEN 'FlightExplore' THEN 7
                    WHEN 'TripAdvisorSearch' THEN 8
                    WHEN 'TripAdvisorPlace' THEN 9
                END
                FROM "TripRadar.Server"."ServiceTypes" st
                WHERE pt."ServiceTypeId" = st."ServiceTypeId";

                UPDATE "TripRadar.Server"."OverageBillingRecords" obr
                SET "ServiceTypeId" = CASE st."Name"
                    WHEN 'Event' THEN 1
                    WHEN 'Flight' THEN 2
                    WHEN 'Hotel' THEN 3
                    WHEN 'LocalPlaces' THEN 4
                    WHEN 'Maps' THEN 5
                    WHEN 'PlaceReview' THEN 6
                    WHEN 'FlightExplore' THEN 7
                    WHEN 'TripAdvisorSearch' THEN 8
                    WHEN 'TripAdvisorPlace' THEN 9
                END
                FROM "TripRadar.Server"."ServiceTypes" st
                WHERE obr."ServiceTypeId" = st."ServiceTypeId";

                UPDATE "TripRadar.Server"."TripQueryHistories" tqh
                SET "ServiceTypeId" = CASE st."Name"
                    WHEN 'Event' THEN 1
                    WHEN 'Flight' THEN 2
                    WHEN 'Hotel' THEN 3
                    WHEN 'LocalPlaces' THEN 4
                    WHEN 'Maps' THEN 5
                    WHEN 'PlaceReview' THEN 6
                    WHEN 'FlightExplore' THEN 7
                    WHEN 'TripAdvisorSearch' THEN 8
                    WHEN 'TripAdvisorPlace' THEN 9
                END
                FROM "TripRadar.Server"."ServiceTypes" st
                WHERE tqh."ServiceTypeId" = st."ServiceTypeId";

                UPDATE "TripRadar.Server"."ServiceTypes"
                SET "ServiceTypeId" = "ServiceTypeId" + 100;

                UPDATE "TripRadar.Server"."ServiceTypes"
                SET "ServiceTypeId" = CASE "Name"
                    WHEN 'Event' THEN 1
                    WHEN 'Flight' THEN 2
                    WHEN 'Hotel' THEN 3
                    WHEN 'LocalPlaces' THEN 4
                    WHEN 'Maps' THEN 5
                    WHEN 'PlaceReview' THEN 6
                    WHEN 'FlightExplore' THEN 7
                    WHEN 'TripAdvisorSearch' THEN 8
                    WHEN 'TripAdvisorPlace' THEN 9
                END;

                SELECT setval(
                    pg_get_serial_sequence('"TripRadar.Server"."ServiceTypes"', 'ServiceTypeId'),
                    COALESCE((SELECT MAX("ServiceTypeId") FROM "TripRadar.Server"."ServiceTypes"), 1)
                );
                """);

            migrationBuilder.AddForeignKey(
                name: "FK_OverageBillingRecords_ServiceTypes_ServiceTypeId",
                schema: "TripRadar.Server",
                table: "OverageBillingRecords",
                column: "ServiceTypeId",
                principalSchema: "TripRadar.Server",
                principalTable: "ServiceTypes",
                principalColumn: "ServiceTypeId",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_PreferenceTypes_ServiceTypes_ServiceTypeId",
                schema: "TripRadar.Server",
                table: "PreferenceTypes",
                column: "ServiceTypeId",
                principalSchema: "TripRadar.Server",
                principalTable: "ServiceTypes",
                principalColumn: "ServiceTypeId",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_ServiceTokenCosts_ServiceTypes_ServiceTypeId",
                schema: "TripRadar.Server",
                table: "ServiceTokenCosts",
                column: "ServiceTypeId",
                principalSchema: "TripRadar.Server",
                principalTable: "ServiceTypes",
                principalColumn: "ServiceTypeId",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_TripQueryHistories_ServiceTypes_ServiceTypeId",
                schema: "TripRadar.Server",
                table: "TripQueryHistories",
                column: "ServiceTypeId",
                principalSchema: "TripRadar.Server",
                principalTable: "ServiceTypes",
                principalColumn: "ServiceTypeId",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.DropTable(
                name: "ScheduledCurrencyExchangeQueries",
                schema: "TripRadar.Server");

            migrationBuilder.DropTable(
                name: "ScheduledWeatherQueries",
                schema: "TripRadar.Server");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_OverageBillingRecords_ServiceTypes_ServiceTypeId",
                schema: "TripRadar.Server",
                table: "OverageBillingRecords");

            migrationBuilder.DropForeignKey(
                name: "FK_PreferenceTypes_ServiceTypes_ServiceTypeId",
                schema: "TripRadar.Server",
                table: "PreferenceTypes");

            migrationBuilder.DropForeignKey(
                name: "FK_ServiceTokenCosts_ServiceTypes_ServiceTypeId",
                schema: "TripRadar.Server",
                table: "ServiceTokenCosts");

            migrationBuilder.DropForeignKey(
                name: "FK_TripQueryHistories_ServiceTypes_ServiceTypeId",
                schema: "TripRadar.Server",
                table: "TripQueryHistories");

            migrationBuilder.Sql(
                """
                UPDATE "TripRadar.Server"."ServiceTokenCosts" stc
                SET "ServiceTypeId" = CASE st."Name"
                    WHEN 'Event' THEN 3
                    WHEN 'Flight' THEN 4
                    WHEN 'Hotel' THEN 6
                    WHEN 'LocalPlaces' THEN 7
                    WHEN 'Maps' THEN 8
                    WHEN 'PlaceReview' THEN 9
                    WHEN 'FlightExplore' THEN 13
                    WHEN 'TripAdvisorSearch' THEN 14
                    WHEN 'TripAdvisorPlace' THEN 15
                END
                FROM "TripRadar.Server"."ServiceTypes" st
                WHERE stc."ServiceTypeId" = st."ServiceTypeId";

                UPDATE "TripRadar.Server"."PreferenceTypes" pt
                SET "ServiceTypeId" = CASE st."Name"
                    WHEN 'Event' THEN 3
                    WHEN 'Flight' THEN 4
                    WHEN 'Hotel' THEN 6
                    WHEN 'LocalPlaces' THEN 7
                    WHEN 'Maps' THEN 8
                    WHEN 'PlaceReview' THEN 9
                    WHEN 'FlightExplore' THEN 13
                    WHEN 'TripAdvisorSearch' THEN 14
                    WHEN 'TripAdvisorPlace' THEN 15
                END
                FROM "TripRadar.Server"."ServiceTypes" st
                WHERE pt."ServiceTypeId" = st."ServiceTypeId";

                UPDATE "TripRadar.Server"."OverageBillingRecords" obr
                SET "ServiceTypeId" = CASE st."Name"
                    WHEN 'Event' THEN 3
                    WHEN 'Flight' THEN 4
                    WHEN 'Hotel' THEN 6
                    WHEN 'LocalPlaces' THEN 7
                    WHEN 'Maps' THEN 8
                    WHEN 'PlaceReview' THEN 9
                    WHEN 'FlightExplore' THEN 13
                    WHEN 'TripAdvisorSearch' THEN 14
                    WHEN 'TripAdvisorPlace' THEN 15
                END
                FROM "TripRadar.Server"."ServiceTypes" st
                WHERE obr."ServiceTypeId" = st."ServiceTypeId";

                UPDATE "TripRadar.Server"."TripQueryHistories" tqh
                SET "ServiceTypeId" = CASE st."Name"
                    WHEN 'Event' THEN 3
                    WHEN 'Flight' THEN 4
                    WHEN 'Hotel' THEN 6
                    WHEN 'LocalPlaces' THEN 7
                    WHEN 'Maps' THEN 8
                    WHEN 'PlaceReview' THEN 9
                    WHEN 'FlightExplore' THEN 13
                    WHEN 'TripAdvisorSearch' THEN 14
                    WHEN 'TripAdvisorPlace' THEN 15
                END
                FROM "TripRadar.Server"."ServiceTypes" st
                WHERE tqh."ServiceTypeId" = st."ServiceTypeId";

                UPDATE "TripRadar.Server"."ServiceTypes"
                SET "ServiceTypeId" = "ServiceTypeId" + 100;

                UPDATE "TripRadar.Server"."ServiceTypes"
                SET "ServiceTypeId" = CASE "Name"
                    WHEN 'Event' THEN 3
                    WHEN 'Flight' THEN 4
                    WHEN 'Hotel' THEN 6
                    WHEN 'LocalPlaces' THEN 7
                    WHEN 'Maps' THEN 8
                    WHEN 'PlaceReview' THEN 9
                    WHEN 'FlightExplore' THEN 13
                    WHEN 'TripAdvisorSearch' THEN 14
                    WHEN 'TripAdvisorPlace' THEN 15
                END;

                SELECT setval(
                    pg_get_serial_sequence('"TripRadar.Server"."ServiceTypes"', 'ServiceTypeId'),
                    COALESCE((SELECT MAX("ServiceTypeId") FROM "TripRadar.Server"."ServiceTypes"), 1)
                );
                """);

            migrationBuilder.AddForeignKey(
                name: "FK_OverageBillingRecords_ServiceTypes_ServiceTypeId",
                schema: "TripRadar.Server",
                table: "OverageBillingRecords",
                column: "ServiceTypeId",
                principalSchema: "TripRadar.Server",
                principalTable: "ServiceTypes",
                principalColumn: "ServiceTypeId",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_PreferenceTypes_ServiceTypes_ServiceTypeId",
                schema: "TripRadar.Server",
                table: "PreferenceTypes",
                column: "ServiceTypeId",
                principalSchema: "TripRadar.Server",
                principalTable: "ServiceTypes",
                principalColumn: "ServiceTypeId",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_ServiceTokenCosts_ServiceTypes_ServiceTypeId",
                schema: "TripRadar.Server",
                table: "ServiceTokenCosts",
                column: "ServiceTypeId",
                principalSchema: "TripRadar.Server",
                principalTable: "ServiceTypes",
                principalColumn: "ServiceTypeId",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_TripQueryHistories_ServiceTypes_ServiceTypeId",
                schema: "TripRadar.Server",
                table: "TripQueryHistories",
                column: "ServiceTypeId",
                principalSchema: "TripRadar.Server",
                principalTable: "ServiceTypes",
                principalColumn: "ServiceTypeId",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.CreateTable(
                name: "ScheduledCurrencyExchangeQueries",
                schema: "TripRadar.Server",
                columns: table => new
                {
                    ScheduledCurrencyExchangeQueryId = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    ScheduledExecutionId = table.Column<long>(type: "bigint", nullable: true),
                    UserId = table.Column<long>(type: "bigint", nullable: false),
                    AdditionalParameters = table.Column<string>(type: "jsonb", nullable: true),
                    BaseCurrency = table.Column<string>(type: "varchar(3)", nullable: false),
                    CreatedOn = table.Column<DateTime>(type: "TIMESTAMPTZ", nullable: false),
                    SelectedColumns = table.Column<string>(type: "jsonb", nullable: true),
                    TargetCurrencies = table.Column<string>(type: "jsonb", nullable: true),
                    UniqueId = table.Column<Guid>(type: "uuid", nullable: false),
                    UpdatedOn = table.Column<DateTime>(type: "TIMESTAMPTZ", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ScheduledCurrencyExchangeQueries", x => x.ScheduledCurrencyExchangeQueryId);
                    table.ForeignKey(
                        name: "FK_ScheduledCurrencyExchangeQueries_ScheduledExecutions_Schedu~",
                        column: x => x.ScheduledExecutionId,
                        principalSchema: "TripRadar.Server",
                        principalTable: "ScheduledExecutions",
                        principalColumn: "ScheduledExecutionId");
                    table.ForeignKey(
                        name: "FK_ScheduledCurrencyExchangeQueries_Users_UserId",
                        column: x => x.UserId,
                        principalSchema: "TripRadar.Server",
                        principalTable: "Users",
                        principalColumn: "UserId",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "ScheduledWeatherQueries",
                schema: "TripRadar.Server",
                columns: table => new
                {
                    ScheduledWeatherQueryId = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    ScheduledExecutionId = table.Column<long>(type: "bigint", nullable: true),
                    UserId = table.Column<long>(type: "bigint", nullable: false),
                    AdditionalParameters = table.Column<string>(type: "jsonb", nullable: true),
                    CityName = table.Column<string>(type: "varchar(255)", nullable: true),
                    CreatedOn = table.Column<DateTime>(type: "TIMESTAMPTZ", nullable: false),
                    Latitude = table.Column<double>(type: "numeric(8,6)", nullable: true),
                    Longitude = table.Column<double>(type: "numeric(9,6)", nullable: true),
                    SelectedColumns = table.Column<string>(type: "jsonb", nullable: true),
                    UniqueId = table.Column<Guid>(type: "uuid", nullable: false),
                    UpdatedOn = table.Column<DateTime>(type: "TIMESTAMPTZ", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ScheduledWeatherQueries", x => x.ScheduledWeatherQueryId);
                    table.ForeignKey(
                        name: "FK_ScheduledWeatherQueries_ScheduledExecutions_ScheduledExecut~",
                        column: x => x.ScheduledExecutionId,
                        principalSchema: "TripRadar.Server",
                        principalTable: "ScheduledExecutions",
                        principalColumn: "ScheduledExecutionId");
                    table.ForeignKey(
                        name: "FK_ScheduledWeatherQueries_Users_UserId",
                        column: x => x.UserId,
                        principalSchema: "TripRadar.Server",
                        principalTable: "Users",
                        principalColumn: "UserId",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ScheduledCurrencyExchangeQueries_ScheduledExecutionId",
                schema: "TripRadar.Server",
                table: "ScheduledCurrencyExchangeQueries",
                column: "ScheduledExecutionId");

            migrationBuilder.CreateIndex(
                name: "IX_ScheduledCurrencyExchangeQueries_UserId",
                schema: "TripRadar.Server",
                table: "ScheduledCurrencyExchangeQueries",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_ScheduledWeatherQueries_ScheduledExecutionId",
                schema: "TripRadar.Server",
                table: "ScheduledWeatherQueries",
                column: "ScheduledExecutionId");

            migrationBuilder.CreateIndex(
                name: "IX_ScheduledWeatherQueries_UserId",
                schema: "TripRadar.Server",
                table: "ScheduledWeatherQueries",
                column: "UserId");
        }
    }
}
