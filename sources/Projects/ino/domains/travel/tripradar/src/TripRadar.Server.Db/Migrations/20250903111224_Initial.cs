using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace TripRadar.Server.Db.Migrations
{
    /// <inheritdoc />
    public partial class Initial : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "TripRadar.Server");

            migrationBuilder.CreateTable(
                name: "Airports",
                schema: "TripRadar.Server",
                columns: table => new
                {
                    AirportId = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    Code = table.Column<string>(type: "varchar(3)", nullable: false),
                    Name = table.Column<string>(type: "varchar(100)", nullable: false),
                    City = table.Column<string>(type: "varchar(100)", nullable: false),
                    Country = table.Column<string>(type: "varchar(100)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Airports", x => x.AirportId);
                });

            migrationBuilder.CreateTable(
                name: "BillingPeriods",
                schema: "TripRadar.Server",
                columns: table => new
                {
                    BillingPeriodId = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    Name = table.Column<string>(type: "varchar(50)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BillingPeriods", x => x.BillingPeriodId);
                });

            migrationBuilder.CreateTable(
                name: "Countries",
                schema: "TripRadar.Server",
                columns: table => new
                {
                    CountryId = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    CountryCode = table.Column<string>(type: "varchar(2)", nullable: false),
                    CountryName = table.Column<string>(type: "varchar(100)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Countries", x => x.CountryId);
                });

            migrationBuilder.CreateTable(
                name: "Currencies",
                schema: "TripRadar.Server",
                columns: table => new
                {
                    CurrencyId = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    CurrencyCode = table.Column<string>(type: "varchar(10)", nullable: false),
                    CurrencyName = table.Column<string>(type: "varchar(100)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Currencies", x => x.CurrencyId);
                });

            migrationBuilder.CreateTable(
                name: "Domains",
                schema: "TripRadar.Server",
                columns: table => new
                {
                    DomainId = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    Domain = table.Column<string>(type: "varchar(100)", nullable: false),
                    LanguageCode = table.Column<string>(type: "varchar(10)", nullable: false),
                    CountryCode = table.Column<string>(type: "varchar(2)", nullable: false),
                    CountryName = table.Column<string>(type: "varchar(100)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Domains", x => x.DomainId);
                });

            migrationBuilder.CreateTable(
                name: "FeedbackCategories",
                schema: "TripRadar.Server",
                columns: table => new
                {
                    FeedbackCategoryId = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    Name = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FeedbackCategories", x => x.FeedbackCategoryId);
                });

            migrationBuilder.CreateTable(
                name: "Languages",
                schema: "TripRadar.Server",
                columns: table => new
                {
                    LanguageId = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    LanguageCode = table.Column<string>(type: "varchar(10)", nullable: false),
                    LanguageName = table.Column<string>(type: "varchar(100)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Languages", x => x.LanguageId);
                });

            migrationBuilder.CreateTable(
                name: "Locations",
                schema: "TripRadar.Server",
                columns: table => new
                {
                    LocationId = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    RowId = table.Column<string>(type: "varchar(24)", nullable: true),
                    GoogleId = table.Column<int>(type: "integer", nullable: true),
                    GoogleParentId = table.Column<int>(type: "integer", nullable: true),
                    Name = table.Column<string>(type: "varchar(100)", nullable: false),
                    CanonicalName = table.Column<string>(type: "varchar(200)", nullable: false),
                    CountryCode = table.Column<string>(type: "varchar(2)", nullable: false),
                    TargetType = table.Column<string>(type: "varchar(50)", nullable: false),
                    Reach = table.Column<int>(type: "integer", nullable: true),
                    GpsLongitude = table.Column<double>(type: "float", nullable: true),
                    GpsLatitude = table.Column<double>(type: "float", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Locations", x => x.LocationId);
                });

            migrationBuilder.CreateTable(
                name: "ServiceTypes",
                schema: "TripRadar.Server",
                columns: table => new
                {
                    ServiceTypeId = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    Name = table.Column<string>(type: "varchar(100)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ServiceTypes", x => x.ServiceTypeId);
                });

            migrationBuilder.CreateTable(
                name: "Tiers",
                schema: "TripRadar.Server",
                columns: table => new
                {
                    TierId = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    Name = table.Column<string>(type: "varchar(50)", nullable: false),
                    TokensPerMonthLimit = table.Column<decimal>(type: "numeric(10,2)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Tiers", x => x.TierId);
                });

            migrationBuilder.CreateTable(
                name: "PreferenceTypes",
                schema: "TripRadar.Server",
                columns: table => new
                {
                    PreferenceTypeId = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    ServiceTypeId = table.Column<int>(type: "integer", nullable: false),
                    Name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    DisplayName = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Description = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    DataType = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    ValidationSchema = table.Column<string>(type: "jsonb", nullable: true),
                    IsRequired = table.Column<bool>(type: "boolean", nullable: false),
                    DefaultValue = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "TIMESTAMPTZ", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "TIMESTAMPTZ", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PreferenceTypes", x => x.PreferenceTypeId);
                    table.ForeignKey(
                        name: "FK_PreferenceTypes_ServiceTypes_ServiceTypeId",
                        column: x => x.ServiceTypeId,
                        principalSchema: "TripRadar.Server",
                        principalTable: "ServiceTypes",
                        principalColumn: "ServiceTypeId",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "ServiceTokenCosts",
                schema: "TripRadar.Server",
                columns: table => new
                {
                    ServiceTokenCostId = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    ServiceTypeId = table.Column<int>(type: "integer", nullable: false),
                    Cost = table.Column<decimal>(type: "numeric(10,2)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ServiceTokenCosts", x => x.ServiceTokenCostId);
                    table.ForeignKey(
                        name: "FK_ServiceTokenCosts_ServiceTypes_ServiceTypeId",
                        column: x => x.ServiceTypeId,
                        principalSchema: "TripRadar.Server",
                        principalTable: "ServiceTypes",
                        principalColumn: "ServiceTypeId",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "OveragePricing",
                schema: "TripRadar.Server",
                columns: table => new
                {
                    OveragePricingId = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    TierId = table.Column<int>(type: "integer", nullable: false),
                    PricePerToken = table.Column<decimal>(type: "numeric(18,6)", nullable: false),
                    CurrencyId = table.Column<int>(type: "integer", nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OveragePricing", x => x.OveragePricingId);
                    table.ForeignKey(
                        name: "FK_OveragePricing_Currencies_CurrencyId",
                        column: x => x.CurrencyId,
                        principalSchema: "TripRadar.Server",
                        principalTable: "Currencies",
                        principalColumn: "CurrencyId",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_OveragePricing_Tiers_TierId",
                        column: x => x.TierId,
                        principalSchema: "TripRadar.Server",
                        principalTable: "Tiers",
                        principalColumn: "TierId",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "Prices",
                schema: "TripRadar.Server",
                columns: table => new
                {
                    PriceId = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    TierId = table.Column<int>(type: "integer", nullable: false),
                    Amount = table.Column<decimal>(type: "numeric(18,2)", nullable: false),
                    BillingPeriodId = table.Column<int>(type: "integer", nullable: false),
                    StripeId = table.Column<string>(type: "varchar(255)", nullable: true),
                    CurrencyId = table.Column<int>(type: "integer", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "TIMESTAMPTZ", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "TIMESTAMPTZ", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Prices", x => x.PriceId);
                    table.ForeignKey(
                        name: "FK_Prices_BillingPeriods_BillingPeriodId",
                        column: x => x.BillingPeriodId,
                        principalSchema: "TripRadar.Server",
                        principalTable: "BillingPeriods",
                        principalColumn: "BillingPeriodId",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_Prices_Currencies_CurrencyId",
                        column: x => x.CurrencyId,
                        principalSchema: "TripRadar.Server",
                        principalTable: "Currencies",
                        principalColumn: "CurrencyId",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_Prices_Tiers_TierId",
                        column: x => x.TierId,
                        principalSchema: "TripRadar.Server",
                        principalTable: "Tiers",
                        principalColumn: "TierId",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "Users",
                schema: "TripRadar.Server",
                columns: table => new
                {
                    UserId = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    Username = table.Column<string>(type: "varchar(255)", nullable: false),
                    Password = table.Column<string>(type: "varchar(255)", nullable: false),
                    Email = table.Column<string>(type: "varchar(255)", nullable: false),
                    IsEmailConfirmed = table.Column<bool>(type: "boolean", nullable: false),
                    EmailConfirmationToken = table.Column<string>(type: "varchar(255)", nullable: true),
                    EmailConfirmationTokenExpiry = table.Column<DateTime>(type: "TIMESTAMPTZ", nullable: true),
                    PasswordResetToken = table.Column<string>(type: "varchar(255)", nullable: true),
                    PasswordResetTokenExpiry = table.Column<DateTime>(type: "TIMESTAMPTZ", nullable: true),
                    FirstName = table.Column<string>(type: "varchar(255)", nullable: true),
                    LastName = table.Column<string>(type: "varchar(255)", nullable: true),
                    PhoneNumber = table.Column<string>(type: "varchar(255)", nullable: true),
                    IpAddress = table.Column<string>(type: "varchar(255)", nullable: false),
                    RefreshToken = table.Column<string>(type: "varchar(255)", nullable: false),
                    RefreshTokenExpiryTime = table.Column<DateTime>(type: "TIMESTAMPTZ", nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    TierId = table.Column<int>(type: "integer", nullable: false),
                    TimeZone = table.Column<string>(type: "varchar(50)", nullable: false),
                    HasDataStorageConsent = table.Column<bool>(type: "boolean", nullable: false),
                    GoogleId = table.Column<string>(type: "varchar(255)", nullable: true),
                    ProfilePictureUrl = table.Column<string>(type: "varchar(500)", nullable: true),
                    CreatedOn = table.Column<DateTime>(type: "TIMESTAMPTZ", nullable: false),
                    UpdatedOn = table.Column<DateTime>(type: "TIMESTAMPTZ", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Users", x => x.UserId);
                    table.ForeignKey(
                        name: "FK_Users_Tiers_TierId",
                        column: x => x.TierId,
                        principalSchema: "TripRadar.Server",
                        principalTable: "Tiers",
                        principalColumn: "TierId",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "Feedbacks",
                schema: "TripRadar.Server",
                columns: table => new
                {
                    FeedbackId = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    UserId = table.Column<long>(type: "bigint", nullable: false),
                    Title = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Content = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: false),
                    Rating = table.Column<int>(type: "integer", nullable: false),
                    CategoryId = table.Column<int>(type: "integer", nullable: false),
                    CreatedOn = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedOn = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Feedbacks", x => x.FeedbackId);
                    table.ForeignKey(
                        name: "FK_Feedbacks_FeedbackCategories_CategoryId",
                        column: x => x.CategoryId,
                        principalSchema: "TripRadar.Server",
                        principalTable: "FeedbackCategories",
                        principalColumn: "FeedbackCategoryId",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_Feedbacks_Users_UserId",
                        column: x => x.UserId,
                        principalSchema: "TripRadar.Server",
                        principalTable: "Users",
                        principalColumn: "UserId",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "OverageBillingRecords",
                schema: "TripRadar.Server",
                columns: table => new
                {
                    OverageBillingRecordId = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    UserId = table.Column<long>(type: "bigint", nullable: false),
                    ServiceTypeId = table.Column<int>(type: "integer", nullable: false),
                    OverageTokensUsed = table.Column<decimal>(type: "numeric(18,6)", nullable: true),
                    TokenUnitCost = table.Column<decimal>(type: "numeric(18,6)", nullable: false),
                    TotalCharge = table.Column<decimal>(type: "numeric(18,2)", nullable: false),
                    CurrencyId = table.Column<int>(type: "integer", nullable: false),
                    Year = table.Column<int>(type: "integer", nullable: false),
                    Month = table.Column<int>(type: "integer", nullable: false),
                    UsageTimestamp = table.Column<DateTime>(type: "TIMESTAMPTZ", nullable: false),
                    IsBilled = table.Column<bool>(type: "boolean", nullable: false),
                    BilledAt = table.Column<DateTime>(type: "TIMESTAMPTZ", nullable: true),
                    StripeInvoiceId = table.Column<string>(type: "varchar(255)", nullable: true),
                    Metadata = table.Column<string>(type: "text", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "TIMESTAMPTZ", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "TIMESTAMPTZ", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OverageBillingRecords", x => x.OverageBillingRecordId);
                    table.ForeignKey(
                        name: "FK_OverageBillingRecords_Currencies_CurrencyId",
                        column: x => x.CurrencyId,
                        principalSchema: "TripRadar.Server",
                        principalTable: "Currencies",
                        principalColumn: "CurrencyId",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_OverageBillingRecords_ServiceTypes_ServiceTypeId",
                        column: x => x.ServiceTypeId,
                        principalSchema: "TripRadar.Server",
                        principalTable: "ServiceTypes",
                        principalColumn: "ServiceTypeId",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_OverageBillingRecords_Users_UserId",
                        column: x => x.UserId,
                        principalSchema: "TripRadar.Server",
                        principalTable: "Users",
                        principalColumn: "UserId",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "ScheduledExecutions",
                schema: "TripRadar.Server",
                columns: table => new
                {
                    ScheduledExecutionId = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    UniqueId = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<long>(type: "bigint", nullable: false),
                    Name = table.Column<string>(type: "varchar(100)", nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    NextExecutionTime = table.Column<DateTime>(type: "TIMESTAMPTZ", nullable: false),
                    Schedule = table.Column<string>(type: "varchar(100)", nullable: false),
                    CreatedOn = table.Column<DateTime>(type: "TIMESTAMPTZ", nullable: false),
                    UpdatedOn = table.Column<DateTime>(type: "TIMESTAMPTZ", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ScheduledExecutions", x => x.ScheduledExecutionId);
                    table.ForeignKey(
                        name: "FK_ScheduledExecutions_Users_UserId",
                        column: x => x.UserId,
                        principalSchema: "TripRadar.Server",
                        principalTable: "Users",
                        principalColumn: "UserId",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "UserMonthlyTokenCounts",
                schema: "TripRadar.Server",
                columns: table => new
                {
                    UserMonthlyTokenCountId = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    UserId = table.Column<long>(type: "bigint", nullable: false),
                    TokensConsumed = table.Column<decimal>(type: "numeric(10,2)", nullable: false),
                    OverageTokensConsumed = table.Column<decimal>(type: "numeric(10,2)", nullable: false),
                    Year = table.Column<int>(type: "integer", nullable: false),
                    Month = table.Column<int>(type: "integer", nullable: false),
                    TimeZone = table.Column<string>(type: "varchar(50)", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "TIMESTAMPTZ", nullable: false),
                    LastUpdateTime = table.Column<DateTime>(type: "TIMESTAMPTZ", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_UserMonthlyTokenCounts", x => x.UserMonthlyTokenCountId);
                    table.ForeignKey(
                        name: "FK_UserMonthlyTokenCounts_Users_UserId",
                        column: x => x.UserId,
                        principalSchema: "TripRadar.Server",
                        principalTable: "Users",
                        principalColumn: "UserId",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "UserPreferences",
                schema: "TripRadar.Server",
                columns: table => new
                {
                    UserPreferenceId = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    UserId = table.Column<long>(type: "bigint", nullable: false),
                    PreferenceTypeId = table.Column<int>(type: "integer", nullable: false),
                    Value = table.Column<string>(type: "jsonb", nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "TIMESTAMPTZ", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "TIMESTAMPTZ", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_UserPreferences", x => x.UserPreferenceId);
                    table.ForeignKey(
                        name: "FK_UserPreferences_PreferenceTypes_PreferenceTypeId",
                        column: x => x.PreferenceTypeId,
                        principalSchema: "TripRadar.Server",
                        principalTable: "PreferenceTypes",
                        principalColumn: "PreferenceTypeId",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_UserPreferences_Users_UserId",
                        column: x => x.UserId,
                        principalSchema: "TripRadar.Server",
                        principalTable: "Users",
                        principalColumn: "UserId",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "UserSubscriptions",
                schema: "TripRadar.Server",
                columns: table => new
                {
                    UserSubscriptionId = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    UserId = table.Column<long>(type: "bigint", nullable: false),
                    StripeCustomerId = table.Column<string>(type: "varchar(255)", nullable: true),
                    StripeSubscriptionId = table.Column<string>(type: "varchar(255)", nullable: true),
                    SubscriptionExpirationTime = table.Column<DateTime>(type: "TIMESTAMPTZ", nullable: true),
                    PendingTierId = table.Column<int>(type: "integer", nullable: true),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    PayAsYouGoEnabled = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "TIMESTAMPTZ", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "TIMESTAMPTZ", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_UserSubscriptions", x => x.UserSubscriptionId);
                    table.ForeignKey(
                        name: "FK_UserSubscriptions_Tiers_PendingTierId",
                        column: x => x.PendingTierId,
                        principalSchema: "TripRadar.Server",
                        principalTable: "Tiers",
                        principalColumn: "TierId");
                    table.ForeignKey(
                        name: "FK_UserSubscriptions_Users_UserId",
                        column: x => x.UserId,
                        principalSchema: "TripRadar.Server",
                        principalTable: "Users",
                        principalColumn: "UserId",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "ScheduledCurrencyExchangeQueries",
                schema: "TripRadar.Server",
                columns: table => new
                {
                    ScheduledCurrencyExchangeQueryId = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    UniqueId = table.Column<Guid>(type: "uuid", nullable: false),
                    BaseCurrency = table.Column<string>(type: "varchar(3)", nullable: false),
                    TargetCurrencies = table.Column<string>(type: "jsonb", nullable: true),
                    ScheduledExecutionId = table.Column<long>(type: "bigint", nullable: true),
                    UserId = table.Column<long>(type: "bigint", nullable: false),
                    CreatedOn = table.Column<DateTime>(type: "TIMESTAMPTZ", nullable: false),
                    UpdatedOn = table.Column<DateTime>(type: "TIMESTAMPTZ", nullable: true),
                    AdditionalParameters = table.Column<string>(type: "jsonb", nullable: true),
                    SelectedColumns = table.Column<string>(type: "jsonb", nullable: true)
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
                name: "ScheduledEventQueries",
                schema: "TripRadar.Server",
                columns: table => new
                {
                    ScheduledEventQueryId = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    UniqueId = table.Column<Guid>(type: "uuid", nullable: false),
                    SearchQuery = table.Column<string>(type: "varchar(255)", nullable: false),
                    ScheduledExecutionId = table.Column<long>(type: "bigint", nullable: true),
                    UserId = table.Column<long>(type: "bigint", nullable: false),
                    CreatedOn = table.Column<DateTime>(type: "TIMESTAMPTZ", nullable: false),
                    UpdatedOn = table.Column<DateTime>(type: "TIMESTAMPTZ", nullable: true),
                    AdditionalParameters = table.Column<string>(type: "jsonb", nullable: true),
                    SelectedColumns = table.Column<string>(type: "jsonb", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ScheduledEventQueries", x => x.ScheduledEventQueryId);
                    table.ForeignKey(
                        name: "FK_ScheduledEventQueries_ScheduledExecutions_ScheduledExecutio~",
                        column: x => x.ScheduledExecutionId,
                        principalSchema: "TripRadar.Server",
                        principalTable: "ScheduledExecutions",
                        principalColumn: "ScheduledExecutionId");
                    table.ForeignKey(
                        name: "FK_ScheduledEventQueries_Users_UserId",
                        column: x => x.UserId,
                        principalSchema: "TripRadar.Server",
                        principalTable: "Users",
                        principalColumn: "UserId",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "ScheduledFlightQueries",
                schema: "TripRadar.Server",
                columns: table => new
                {
                    ScheduledFlightQueryId = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    UniqueId = table.Column<Guid>(type: "uuid", nullable: false),
                    DepartureAirportId = table.Column<int>(type: "integer", nullable: false),
                    DestinationAirportId = table.Column<int>(type: "integer", nullable: false),
                    ScheduledExecutionId = table.Column<long>(type: "bigint", nullable: true),
                    UserId = table.Column<long>(type: "bigint", nullable: false),
                    DepartureDate = table.Column<DateTime>(type: "TIMESTAMPTZ", nullable: false),
                    ReturnDate = table.Column<DateTime>(type: "TIMESTAMPTZ", nullable: true),
                    CreatedOn = table.Column<DateTime>(type: "TIMESTAMPTZ", nullable: false),
                    UpdatedOn = table.Column<DateTime>(type: "TIMESTAMPTZ", nullable: true),
                    AdditionalParameters = table.Column<string>(type: "jsonb", nullable: true),
                    SelectedColumns = table.Column<string>(type: "jsonb", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ScheduledFlightQueries", x => x.ScheduledFlightQueryId);
                    table.ForeignKey(
                        name: "FK_ScheduledFlightQueries_Airports_DepartureAirportId",
                        column: x => x.DepartureAirportId,
                        principalSchema: "TripRadar.Server",
                        principalTable: "Airports",
                        principalColumn: "AirportId",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_ScheduledFlightQueries_Airports_DestinationAirportId",
                        column: x => x.DestinationAirportId,
                        principalSchema: "TripRadar.Server",
                        principalTable: "Airports",
                        principalColumn: "AirportId",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_ScheduledFlightQueries_ScheduledExecutions_ScheduledExecuti~",
                        column: x => x.ScheduledExecutionId,
                        principalSchema: "TripRadar.Server",
                        principalTable: "ScheduledExecutions",
                        principalColumn: "ScheduledExecutionId");
                    table.ForeignKey(
                        name: "FK_ScheduledFlightQueries_Users_UserId",
                        column: x => x.UserId,
                        principalSchema: "TripRadar.Server",
                        principalTable: "Users",
                        principalColumn: "UserId",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "ScheduledHotelQueries",
                schema: "TripRadar.Server",
                columns: table => new
                {
                    ScheduledHotelQueryId = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    UniqueId = table.Column<Guid>(type: "uuid", nullable: false),
                    Location = table.Column<string>(type: "varchar(255)", nullable: false),
                    ScheduledExecutionId = table.Column<long>(type: "bigint", nullable: true),
                    UserId = table.Column<long>(type: "bigint", nullable: false),
                    CheckInDate = table.Column<DateTime>(type: "TIMESTAMPTZ", nullable: false),
                    CheckOutDate = table.Column<DateTime>(type: "TIMESTAMPTZ", nullable: false),
                    CreatedOn = table.Column<DateTime>(type: "TIMESTAMPTZ", nullable: false),
                    UpdatedOn = table.Column<DateTime>(type: "TIMESTAMPTZ", nullable: true),
                    AdditionalParameters = table.Column<string>(type: "jsonb", nullable: true),
                    SelectedColumns = table.Column<string>(type: "jsonb", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ScheduledHotelQueries", x => x.ScheduledHotelQueryId);
                    table.ForeignKey(
                        name: "FK_ScheduledHotelQueries_ScheduledExecutions_ScheduledExecutio~",
                        column: x => x.ScheduledExecutionId,
                        principalSchema: "TripRadar.Server",
                        principalTable: "ScheduledExecutions",
                        principalColumn: "ScheduledExecutionId");
                    table.ForeignKey(
                        name: "FK_ScheduledHotelQueries_Users_UserId",
                        column: x => x.UserId,
                        principalSchema: "TripRadar.Server",
                        principalTable: "Users",
                        principalColumn: "UserId",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "ScheduledLocalPlacesQueries",
                schema: "TripRadar.Server",
                columns: table => new
                {
                    ScheduledLocalPlacesQueryId = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    UniqueId = table.Column<Guid>(type: "uuid", nullable: false),
                    SearchQuery = table.Column<string>(type: "varchar(500)", nullable: false),
                    ScheduledExecutionId = table.Column<long>(type: "bigint", nullable: true),
                    UserId = table.Column<long>(type: "bigint", nullable: false),
                    CreatedOn = table.Column<DateTime>(type: "TIMESTAMPTZ", nullable: false),
                    UpdatedOn = table.Column<DateTime>(type: "TIMESTAMPTZ", nullable: true),
                    AdditionalParameters = table.Column<string>(type: "jsonb", nullable: true),
                    SelectedColumns = table.Column<string>(type: "jsonb", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ScheduledLocalPlacesQueries", x => x.ScheduledLocalPlacesQueryId);
                    table.ForeignKey(
                        name: "FK_ScheduledLocalPlacesQueries_ScheduledExecutions_ScheduledEx~",
                        column: x => x.ScheduledExecutionId,
                        principalSchema: "TripRadar.Server",
                        principalTable: "ScheduledExecutions",
                        principalColumn: "ScheduledExecutionId");
                    table.ForeignKey(
                        name: "FK_ScheduledLocalPlacesQueries_Users_UserId",
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
                    UniqueId = table.Column<Guid>(type: "uuid", nullable: false),
                    CityName = table.Column<string>(type: "varchar(255)", nullable: true),
                    Latitude = table.Column<double>(type: "numeric(8,6)", nullable: true),
                    Longitude = table.Column<double>(type: "numeric(9,6)", nullable: true),
                    ScheduledExecutionId = table.Column<long>(type: "bigint", nullable: true),
                    UserId = table.Column<long>(type: "bigint", nullable: false),
                    CreatedOn = table.Column<DateTime>(type: "TIMESTAMPTZ", nullable: false),
                    UpdatedOn = table.Column<DateTime>(type: "TIMESTAMPTZ", nullable: true),
                    AdditionalParameters = table.Column<string>(type: "jsonb", nullable: true),
                    SelectedColumns = table.Column<string>(type: "jsonb", nullable: true)
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
                name: "IX_Feedbacks_CategoryId",
                schema: "TripRadar.Server",
                table: "Feedbacks",
                column: "CategoryId");

            migrationBuilder.CreateIndex(
                name: "IX_Feedbacks_UserId",
                schema: "TripRadar.Server",
                table: "Feedbacks",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_OverageBillingRecords_CurrencyId",
                schema: "TripRadar.Server",
                table: "OverageBillingRecords",
                column: "CurrencyId");

            migrationBuilder.CreateIndex(
                name: "IX_OverageBillingRecords_ServiceTypeId",
                schema: "TripRadar.Server",
                table: "OverageBillingRecords",
                column: "ServiceTypeId");

            migrationBuilder.CreateIndex(
                name: "IX_OverageBillingRecords_UserId",
                schema: "TripRadar.Server",
                table: "OverageBillingRecords",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_OveragePricing_CurrencyId",
                schema: "TripRadar.Server",
                table: "OveragePricing",
                column: "CurrencyId");

            migrationBuilder.CreateIndex(
                name: "IX_OveragePricing_TierId",
                schema: "TripRadar.Server",
                table: "OveragePricing",
                column: "TierId");

            migrationBuilder.CreateIndex(
                name: "IX_PreferenceTypes_ServiceTypeId",
                schema: "TripRadar.Server",
                table: "PreferenceTypes",
                column: "ServiceTypeId");

            migrationBuilder.CreateIndex(
                name: "IX_Prices_BillingPeriodId",
                schema: "TripRadar.Server",
                table: "Prices",
                column: "BillingPeriodId");

            migrationBuilder.CreateIndex(
                name: "IX_Prices_CurrencyId",
                schema: "TripRadar.Server",
                table: "Prices",
                column: "CurrencyId");

            migrationBuilder.CreateIndex(
                name: "IX_Prices_TierId",
                schema: "TripRadar.Server",
                table: "Prices",
                column: "TierId");

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
                name: "IX_ScheduledEventQueries_ScheduledExecutionId",
                schema: "TripRadar.Server",
                table: "ScheduledEventQueries",
                column: "ScheduledExecutionId");

            migrationBuilder.CreateIndex(
                name: "IX_ScheduledEventQueries_UserId",
                schema: "TripRadar.Server",
                table: "ScheduledEventQueries",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_ScheduledExecutions_UserId",
                schema: "TripRadar.Server",
                table: "ScheduledExecutions",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_ScheduledFlightQueries_DepartureAirportId",
                schema: "TripRadar.Server",
                table: "ScheduledFlightQueries",
                column: "DepartureAirportId");

            migrationBuilder.CreateIndex(
                name: "IX_ScheduledFlightQueries_DestinationAirportId",
                schema: "TripRadar.Server",
                table: "ScheduledFlightQueries",
                column: "DestinationAirportId");

            migrationBuilder.CreateIndex(
                name: "IX_ScheduledFlightQueries_ScheduledExecutionId",
                schema: "TripRadar.Server",
                table: "ScheduledFlightQueries",
                column: "ScheduledExecutionId");

            migrationBuilder.CreateIndex(
                name: "IX_ScheduledFlightQueries_UserId",
                schema: "TripRadar.Server",
                table: "ScheduledFlightQueries",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_ScheduledHotelQueries_ScheduledExecutionId",
                schema: "TripRadar.Server",
                table: "ScheduledHotelQueries",
                column: "ScheduledExecutionId");

            migrationBuilder.CreateIndex(
                name: "IX_ScheduledHotelQueries_UserId",
                schema: "TripRadar.Server",
                table: "ScheduledHotelQueries",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_ScheduledLocalPlacesQueries_ScheduledExecutionId",
                schema: "TripRadar.Server",
                table: "ScheduledLocalPlacesQueries",
                column: "ScheduledExecutionId");

            migrationBuilder.CreateIndex(
                name: "IX_ScheduledLocalPlacesQueries_UserId",
                schema: "TripRadar.Server",
                table: "ScheduledLocalPlacesQueries",
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

            migrationBuilder.CreateIndex(
                name: "IX_ServiceTokenCosts_ServiceTypeId",
                schema: "TripRadar.Server",
                table: "ServiceTokenCosts",
                column: "ServiceTypeId");

            migrationBuilder.CreateIndex(
                name: "IX_UserMonthlyTokenCounts_UserId",
                schema: "TripRadar.Server",
                table: "UserMonthlyTokenCounts",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_UserPreferences_PreferenceTypeId",
                schema: "TripRadar.Server",
                table: "UserPreferences",
                column: "PreferenceTypeId");

            migrationBuilder.CreateIndex(
                name: "IX_UserPreferences_UserId",
                schema: "TripRadar.Server",
                table: "UserPreferences",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_Users_TierId",
                schema: "TripRadar.Server",
                table: "Users",
                column: "TierId");

            migrationBuilder.CreateIndex(
                name: "IX_UserSubscriptions_PendingTierId",
                schema: "TripRadar.Server",
                table: "UserSubscriptions",
                column: "PendingTierId");

            migrationBuilder.CreateIndex(
                name: "IX_UserSubscriptions_UserId",
                schema: "TripRadar.Server",
                table: "UserSubscriptions",
                column: "UserId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "Countries",
                schema: "TripRadar.Server");

            migrationBuilder.DropTable(
                name: "Domains",
                schema: "TripRadar.Server");

            migrationBuilder.DropTable(
                name: "Feedbacks",
                schema: "TripRadar.Server");

            migrationBuilder.DropTable(
                name: "Languages",
                schema: "TripRadar.Server");

            migrationBuilder.DropTable(
                name: "Locations",
                schema: "TripRadar.Server");

            migrationBuilder.DropTable(
                name: "OverageBillingRecords",
                schema: "TripRadar.Server");

            migrationBuilder.DropTable(
                name: "OveragePricing",
                schema: "TripRadar.Server");

            migrationBuilder.DropTable(
                name: "Prices",
                schema: "TripRadar.Server");

            migrationBuilder.DropTable(
                name: "ScheduledCurrencyExchangeQueries",
                schema: "TripRadar.Server");

            migrationBuilder.DropTable(
                name: "ScheduledEventQueries",
                schema: "TripRadar.Server");

            migrationBuilder.DropTable(
                name: "ScheduledFlightQueries",
                schema: "TripRadar.Server");

            migrationBuilder.DropTable(
                name: "ScheduledHotelQueries",
                schema: "TripRadar.Server");

            migrationBuilder.DropTable(
                name: "ScheduledLocalPlacesQueries",
                schema: "TripRadar.Server");

            migrationBuilder.DropTable(
                name: "ScheduledWeatherQueries",
                schema: "TripRadar.Server");

            migrationBuilder.DropTable(
                name: "ServiceTokenCosts",
                schema: "TripRadar.Server");

            migrationBuilder.DropTable(
                name: "UserMonthlyTokenCounts",
                schema: "TripRadar.Server");

            migrationBuilder.DropTable(
                name: "UserPreferences",
                schema: "TripRadar.Server");

            migrationBuilder.DropTable(
                name: "UserSubscriptions",
                schema: "TripRadar.Server");

            migrationBuilder.DropTable(
                name: "FeedbackCategories",
                schema: "TripRadar.Server");

            migrationBuilder.DropTable(
                name: "BillingPeriods",
                schema: "TripRadar.Server");

            migrationBuilder.DropTable(
                name: "Currencies",
                schema: "TripRadar.Server");

            migrationBuilder.DropTable(
                name: "Airports",
                schema: "TripRadar.Server");

            migrationBuilder.DropTable(
                name: "ScheduledExecutions",
                schema: "TripRadar.Server");

            migrationBuilder.DropTable(
                name: "PreferenceTypes",
                schema: "TripRadar.Server");

            migrationBuilder.DropTable(
                name: "Users",
                schema: "TripRadar.Server");

            migrationBuilder.DropTable(
                name: "ServiceTypes",
                schema: "TripRadar.Server");

            migrationBuilder.DropTable(
                name: "Tiers",
                schema: "TripRadar.Server");
        }
    }
}
