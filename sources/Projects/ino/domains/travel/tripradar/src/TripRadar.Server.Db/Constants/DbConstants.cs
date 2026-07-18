namespace TripRadar.Server.Db.Constants;

public static class DbConstants
{
    // Schema
    public const string SchemaName = "TripRadar.Server";

    // Tables
    public static class Tables
    {
        // Common tables
        public const string Users = "Users";
        public const string UserProfiles = "UserProfiles";
        public const string Tiers = "Tiers";
        public const string BillingPeriods = "BillingPeriods";
        public const string Prices = "Prices";
        public const string Feedbacks = "Feedbacks";
        public const string FeedbackCategories = "FeedbackCategories";
        public const string UserSubscriptions = "UserSubscriptions";
        public const string OverageBillingRecords = "OverageBillingRecords";
        public const string OveragePricing = "OveragePricing";
        public const string UserPreferences = "UserPreferences";
        public const string PreferenceTypes = "PreferenceTypes";
        public const string PreferenceCategories = "PreferenceCategories";
        public const string UserMonthlyTokenCounts = "UserMonthlyTokenCounts";

        // Lookup/reference tables
        public const string Airports = "Airports";
        public const string Countries = "Countries";
        public const string Currencies = "Currencies";
        public const string Domains = "Domains";
        public const string TripAdvisorDomains = "TripAdvisorDomains";
        public const string OpenTableDomains = "OpenTableDomains";
        public const string YelpDomains = "YelpDomains";
        public const string YelpReviewLanguages = "YelpReviewLanguages";
        public const string GoogleLrLanguages = "GoogleLrLanguages";
        public const string Languages = "Languages";
        public const string Timezones = "Timezones";
        public const string Airlines = "Airlines";
        public const string Locations = "Locations";
        public const string ServiceTypes = "ServiceTypes";
        public const string UsageEventSources = "UsageEventSources";
        public const string UsageEvents = "UsageEvents";
        public const string ServiceTokenCosts = "ServiceTokenCosts";
        public const string DiscountTypes = "DiscountTypes";
        public const string PromoCodes = "PromoCodes";
        public const string PromoCodeUsages = "PromoCodeUsages";

        // Scheduled execution tables
        public const string ScheduledExecutions = "ScheduledExecutions";
        public const string ScheduledFlightQueries = "ScheduledFlightQueries";
        public const string ScheduledHotelQueries = "ScheduledHotelQueries";
        public const string ScheduledEventQueries = "ScheduledEventQueries";
        public const string ScheduledLocalPlacesQueries = "ScheduledLocalPlacesQueries";

        // Trip Vault tables
        public const string TripVaults = "TripVaults";
        public const string TripQueryHistories = "TripQueryHistories";
    }

    public static class SeedFiles
    {
        public const string Airports = "airports.json";
        public const string Countries = "google-countries.json";
        public const string Languages = "google-languages.json";
        public const string GoogleLrLanguages = "google-lr-languages.json";
        public const string Domains = "google-domains.json";
        public const string TripAdvisorDomains = "tripadvisor-domains.json";
        public const string OpenTableDomains = "open-table-domains.json";
        public const string YelpDomains = "yelp-domains.json";
        public const string YelpReviewLanguages = "yelp-reviews-languages.json";
        public const string Currencies = "google-travel-currencies.json";
        public const string Airlines = "airlines.json";
        public const string Locations = "locations.json";
    }

    public static class SeedDefaults
    {
        public const string DefaultCurrencyCode = "usd";
    }

    /// <summary>
    /// Validation-related constants for schema constraints
    /// </summary>
    public static class Validations
    {
        /// <summary>
        /// Common string length limits for HasMaxLength
        /// </summary>
        public static class MaxLengths
        {
            public const int L2 = 2;
            public const int L3 = 3;
            public const int L10 = 10;
            public const int L32 = 32;
            public const int L50 = 50;
            public const int L64 = 64;
            public const int L100 = 100;
            public const int L200 = 200;
            public const int L255 = 255;
            public const int L500 = 500;
            public const int L2000 = 2000;
        }
    }

    /// <summary>
    /// Database column type constants organized by category
    /// </summary>
    public static class ColumnTypes
    {
        /// <summary>
        /// String/Text column types
        /// </summary>
        public static class Text
        {
            public const string Varchar3 = "varchar(3)";
            public const string Varchar2 = "varchar(2)";
            public const string Varchar24 = "varchar(24)";
            public const string Varchar10 = "varchar(10)";
            public const string Varchar32 = "varchar(32)";
            public const string Varchar50 = "varchar(50)";
            public const string Varchar64 = "varchar(64)";
            public const string Varchar100 = "varchar(100)";
            public const string Varchar200 = "varchar(200)";
            public const string Varchar255 = "varchar(255)";
            public const string Varchar500 = "varchar(500)";
            public const string TextType = "text";
        }

        /// <summary>
        /// Numeric column types
        /// </summary>
        public static class Numeric
        {
            public const string Integer = "integer";
            public const string BigInt = "bigint";
            public const string Float = "float";
            public const string Decimal8_6 = "decimal(8,6)";
            public const string Decimal9_6 = "decimal(9,6)";
            public const string Decimal18_6 = "decimal(18,6)";
            public const string Decimal18_4 = "decimal(18,4)";
            public const string Decimal18_2 = "decimal(18,2)";
            public const string Decimal10_2 = "decimal(10,2)";
        }

        /// <summary>
        /// Date/Time column types
        /// </summary>
        public static class DateTime
        {
            public const string TimestampTz = "TIMESTAMPTZ";
        }

        /// <summary>
        /// Boolean column types
        /// </summary>
        public static class Boolean
        {
            public const string BooleanType = "boolean";
        }

        /// <summary>
        /// JSON column types
        /// </summary>
        public static class Json
        {
            public const string Jsonb = "jsonb";
        }

        /// <summary>
        /// UUID column types
        /// </summary>
        public static class Identifier
        {
            public const string Uuid = "uuid";
        }

        /// <summary>
        /// Common SQL default value expressions
        /// </summary>
        public static class DefaultValueSql
        {
            public const string Now = "NOW()";
        }
    }
}
