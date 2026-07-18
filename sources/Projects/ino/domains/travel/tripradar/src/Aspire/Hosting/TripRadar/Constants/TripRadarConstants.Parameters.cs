namespace Aspire.Hosting.TripRadar.Constants;

internal static partial class TripRadarConstants
{
    internal static class ParameterNames
    {
        public const string JwtSecret = "tripradar-jwt-secret";
        public const string JwtRefreshTokenSecret = "tripradar-jwt-refresh-token-secret";
        public const string EncryptionKey = "tripradar-encryption-key";
        public const string ApiKey = "tripradar-api-key";
        public const string SiloGraphQlBearerToken = "tripradar-silo-graphql-bearer-token";
        public const string InternalApiKey = "tripradar-internal-api-key";
        public const string AppSecret = "tripradar-app-secret";
        public const string HangfireAdminPassword = "tripradar-hangfire-admin-password";
        public const string PostgresPassword = "tripradar-postgres-password";
        public const string StripeSecretKey = "tripradar-stripe-secret-key";
        public const string StripePublishableKey = "tripradar-stripe-publishable-key";
        public const string GoogleClientId = "tripradar-google-client-id";
        public const string GoogleClientSecret = "tripradar-google-client-secret";
        public const string TelegramClientId = "tripradar-telegram-client-id";
        public const string TelegramClientSecret = "tripradar-telegram-client-secret";
        public const string SerpApiKey = "tripradar-serp-api-key";
        public const string EmailConnectionString = "tripradar-email-connection-string";
        public const string CorsOrigins = "tripradar-cors-origins";
        public const string StripeAllowUnverifiedWebhooksInDevelopment = "tripradar-stripe-allow-unverified-webhooks-in-development";
        public const string StripeSuccessUrl = "tripradar-stripe-success-url";
        public const string StripeCancelUrl = "tripradar-stripe-cancel-url";
        public const string StripeBasicTierPriceId = "tripradar-stripe-basic-tier-price-id";
        public const string StripeEssentialTierPriceId = "tripradar-stripe-essential-tier-price-id";
        public const string StripeAdvancedTierPriceId = "tripradar-stripe-advanced-tier-price-id";
        public const string StripeBasicTierYearlyPriceId = "tripradar-stripe-basic-tier-yearly-price-id";
        public const string StripeEssentialTierYearlyPriceId = "tripradar-stripe-essential-tier-yearly-price-id";
        public const string StripeAdvancedTierYearlyPriceId = "tripradar-stripe-advanced-tier-yearly-price-id";
        public const string EmailSenderName = "tripradar-email-sender-name";
        public const string EmailSenderEmail = "tripradar-email-sender-email";
        public const string EmailApiBaseUrl = "tripradar-email-api-base-url";
        public const string RedirectUrl = "tripradar-redirect-url";
        public const string EmailLogoUrl = "tripradar-email-logo-url";
        public const string BlobStorageUrl = "tripradar-blob-storage-url";
        public const string BlobStorageSasToken = "tripradar-blob-storage-sas-token";
        public const string HangfireAdminUsername = "tripradar-hangfire-admin-username";
        public const string TelegramBotToken = "telegram-bot-token";
        public const string TelegramSessionSyncSecret = "telegram-session-sync-secret";
        public const string DevTelegramUserId = "dev-telegram-user-id";
        public const string DevTelegramHandle = "dev-telegram-handle";
    }

    internal static class ParameterDefaults
    {
        public const string JwtSecret = "1Sztt4F5b5eYTC0A7dXR3xKevAu7FCbk";
        public const string JwtRefreshTokenSecret = "EC3F1heJRhw93K4NAn0zQ8T6etEuyccK";
        public const string EncryptionKey = "hKsBbM5s63hPdk4kUAHTd6D5qgD1P2Cy";
        public const string ApiKey = "p3Em2h9aaTEXwEY886gVhB6uT5MsmAaH";
        public const string GraphQlBearerToken = "Bp1akMstEe3Bvxn0S0GG41zrg7B7KUGH";
        public const string InternalApiKey = "uv1v75HccFQr66T6vDkPWCTMWD56tcmp";
        public const string AppSecret = "79E8s1jXjAm8cUezYrXu7n09PFSgASTm";
        public const string HangfireAdminPassword = "UV0Urxh2yYrC2hd47Z8Gg3fMTKSvT1jg";
        public const string PostgresPassword = "4Dep0yment!.";
        public const string CorsOrigins = "http://localhost:3000,https://localhost:3000,http://localhost:5173,https://localhost:5173,http://127.0.0.1:3000,https://127.0.0.1:3000,http://127.0.0.1:5173,https://127.0.0.1:5173";
        public const string StripeAllowUnverifiedWebhooksInDevelopment = "true";
        public const string StripeSuccessUrl = "https://localhost:3000/payment/success?session_id={CHECKOUT_SESSION_ID}";
        public const string StripeCancelUrl = "https://localhost:3000/payment/cancel";
        public const string StripeBasicTierPriceId = "price_1RU1TfB35G5bu7pWQDL1ypJ2";
        public const string StripeEssentialTierPriceId = "price_1T30QtB35G5bu7pWMB6dRBYM";
        public const string StripeAdvancedTierPriceId = "price_1T30S8B35G5bu7pWz6a0gwqZ";
        public const string StripeBasicTierYearlyPriceId = "price_1RU1TfB35G5bu7pWQDL1ypJ2";
        public const string StripeEssentialTierYearlyPriceId = "price_1T30VXB35G5bu7pWNKnogc7N";
        public const string StripeAdvancedTierYearlyPriceId = "price_1T30WIB35G5bu7pWvmluxnHJ";
        public const string EmailSenderName = "Trip Radar";
        public const string EmailSenderEmail = "DoNotReply@tripradar.io";
        public const string EmailApiBaseUrl = "http://localhost:5330";
        public const string RedirectUrl = "https://localhost:3000";
        public const string EmailLogoUrl = "tripradar-logo-brand.png";
        public const string HangfireAdminUsername = "admin";
    }
}