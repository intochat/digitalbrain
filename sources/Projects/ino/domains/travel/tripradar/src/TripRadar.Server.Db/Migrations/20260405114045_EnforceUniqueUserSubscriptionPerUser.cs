using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TripRadar.Server.Db.Migrations
{
    /// <inheritdoc />
    public partial class EnforceUniqueUserSubscriptionPerUser : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                """
                WITH ranked AS (
                    SELECT
                        us."UserSubscriptionId",
                        us."UserId",
                        us."StripeCustomerId",
                        us."StripeSubscriptionId",
                        us."SubscriptionExpirationTime",
                        us."PendingTierId",
                        us."IsActive",
                        us."PayAsYouGoEnabled",
                        us."DeferredDowngradeJobId",
                        us."CreatedAt",
                        us."UpdatedAt",
                        ROW_NUMBER() OVER (
                            PARTITION BY us."UserId"
                            ORDER BY
                                CASE WHEN us."StripeSubscriptionId" IS NOT NULL AND BTRIM(us."StripeSubscriptionId") <> '' THEN 1 ELSE 0 END DESC,
                                CASE WHEN us."StripeCustomerId" IS NOT NULL AND BTRIM(us."StripeCustomerId") <> '' THEN 1 ELSE 0 END DESC,
                                CASE WHEN us."SubscriptionExpirationTime" IS NOT NULL THEN 1 ELSE 0 END DESC,
                                COALESCE(us."UpdatedAt", us."CreatedAt") DESC,
                                us."UserSubscriptionId" DESC
                        ) AS row_num
                    FROM "TripRadar.Server"."UserSubscriptions" us
                ),
                best AS (
                    SELECT DISTINCT ON (r."UserId")
                        r."UserId",
                        r."UserSubscriptionId" AS "KeepUserSubscriptionId",
                        r."StripeCustomerId",
                        r."StripeSubscriptionId",
                        r."SubscriptionExpirationTime",
                        r."PendingTierId",
                        r."DeferredDowngradeJobId"
                    FROM ranked r
                    ORDER BY r."UserId", r.row_num
                ),
                aggregated AS (
                    SELECT
                        r."UserId",
                        MIN(r."CreatedAt") AS "CreatedAt",
                        MAX(r."UpdatedAt") AS "UpdatedAt",
                        BOOL_OR(r."IsActive") AS "IsActive",
                        BOOL_OR(r."PayAsYouGoEnabled") AS "PayAsYouGoEnabled",
                        MAX(r."SubscriptionExpirationTime") AS "SubscriptionExpirationTime",
                        MAX(r."PendingTierId") FILTER (WHERE r."PendingTierId" IS NOT NULL) AS "PendingTierId",
                        MAX(r."DeferredDowngradeJobId") FILTER (
                            WHERE r."DeferredDowngradeJobId" IS NOT NULL AND BTRIM(r."DeferredDowngradeJobId") <> ''
                        ) AS "DeferredDowngradeJobId",
                        MAX(r."StripeCustomerId") FILTER (
                            WHERE r."StripeCustomerId" IS NOT NULL AND BTRIM(r."StripeCustomerId") <> ''
                        ) AS "StripeCustomerId",
                        MAX(r."StripeSubscriptionId") FILTER (
                            WHERE r."StripeSubscriptionId" IS NOT NULL AND BTRIM(r."StripeSubscriptionId") <> ''
                        ) AS "StripeSubscriptionId"
                    FROM ranked r
                    GROUP BY r."UserId"
                )
                UPDATE "TripRadar.Server"."UserSubscriptions" target
                SET
                    "StripeCustomerId" = COALESCE(best."StripeCustomerId", aggregated."StripeCustomerId"),
                    "StripeSubscriptionId" = COALESCE(best."StripeSubscriptionId", aggregated."StripeSubscriptionId"),
                    "SubscriptionExpirationTime" = COALESCE(best."SubscriptionExpirationTime", aggregated."SubscriptionExpirationTime"),
                    "PendingTierId" = COALESCE(best."PendingTierId", aggregated."PendingTierId"),
                    "IsActive" = aggregated."IsActive",
                    "PayAsYouGoEnabled" = aggregated."PayAsYouGoEnabled",
                    "DeferredDowngradeJobId" = COALESCE(best."DeferredDowngradeJobId", aggregated."DeferredDowngradeJobId"),
                    "CreatedAt" = aggregated."CreatedAt",
                    "UpdatedAt" = COALESCE(aggregated."UpdatedAt", target."UpdatedAt")
                FROM best
                INNER JOIN aggregated ON aggregated."UserId" = best."UserId"
                WHERE target."UserSubscriptionId" = best."KeepUserSubscriptionId";

                WITH ranked AS (
                    SELECT
                        us."UserSubscriptionId",
                        ROW_NUMBER() OVER (
                            PARTITION BY us."UserId"
                            ORDER BY
                                CASE WHEN us."StripeSubscriptionId" IS NOT NULL AND BTRIM(us."StripeSubscriptionId") <> '' THEN 1 ELSE 0 END DESC,
                                CASE WHEN us."StripeCustomerId" IS NOT NULL AND BTRIM(us."StripeCustomerId") <> '' THEN 1 ELSE 0 END DESC,
                                CASE WHEN us."SubscriptionExpirationTime" IS NOT NULL THEN 1 ELSE 0 END DESC,
                                COALESCE(us."UpdatedAt", us."CreatedAt") DESC,
                                us."UserSubscriptionId" DESC
                        ) AS row_num
                    FROM "TripRadar.Server"."UserSubscriptions" us
                )
                DELETE FROM "TripRadar.Server"."UserSubscriptions" duplicates
                USING ranked
                WHERE duplicates."UserSubscriptionId" = ranked."UserSubscriptionId"
                  AND ranked.row_num > 1;
                """);

            migrationBuilder.DropIndex(
                name: "IX_UserSubscriptions_UserId",
                schema: "TripRadar.Server",
                table: "UserSubscriptions");

            migrationBuilder.CreateIndex(
                name: "IX_UserSubscriptions_UserId",
                schema: "TripRadar.Server",
                table: "UserSubscriptions",
                column: "UserId",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_UserSubscriptions_UserId",
                schema: "TripRadar.Server",
                table: "UserSubscriptions");

            migrationBuilder.CreateIndex(
                name: "IX_UserSubscriptions_UserId",
                schema: "TripRadar.Server",
                table: "UserSubscriptions",
                column: "UserId");
        }
    }
}

