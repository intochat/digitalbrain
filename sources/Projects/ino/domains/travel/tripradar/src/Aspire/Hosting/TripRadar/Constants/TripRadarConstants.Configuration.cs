namespace Aspire.Hosting.TripRadar.Constants;

internal static partial class TripRadarConstants
{
    internal static class ConfigurationKeys
    {
        public const string AspNetCoreEnvironment = "ASPNETCORE_ENVIRONMENT";
        public const string DotNetEnvironment = "DOTNET_ENVIRONMENT";
        public const string TripRadarDbAllowSchemaResetOnRelationExists = "TRIPRADAR_DB_ALLOW_SCHEMA_RESET_ON_RELATION_EXISTS";
        public const string JwtKey = "Jwt__Key";
        public const string JwtRefreshTokenKey = "Jwt__RefreshTokenKey";
        public const string JwtIssuer = "Jwt__Issuer";
        public const string JwtAudience = "Jwt__Audience";
        public const string JwtDurationInMinutes = "Jwt__DurationInMinutes";
        public const string EncryptionUserDataKey = "Encryption__UserDataKey";
        public const string ApiKey = "ApiKey";
        public const string InternalApiKey = "InternalApiKey";
        public const string CorsOriginsWhiteList = "CorsOriginsWhiteList";
        public const string GoogleAuthClientId = "GoogleAuth__ClientId";
        public const string GoogleAuthClientSecret = "GoogleAuth__ClientSecret";
        public const string SerpApiSettingsApiKey = "SerpApiSettings__ApiKey";
        public const string PaymentSettingsStripeSecretKey = "PaymentSettings__Stripe__SecretKey";
        public const string PaymentSettingsStripePublishableKey = "PaymentSettings__Stripe__PublishableKey";
        public const string PaymentSettingsStripeAllowUnverifiedWebhooksInDevelopment = "PaymentSettings__Stripe__AllowUnverifiedWebhooksInDevelopment";
        public const string PaymentSettingsStripeSuccessUrl = "PaymentSettings__Stripe__SuccessUrl";
        public const string PaymentSettingsStripeCancelUrl = "PaymentSettings__Stripe__CancelUrl";
        public const string PaymentSettingsStripeWebhookSecret = "PaymentSettings__Stripe__WebhookSecret";
        public const string PaymentSettingsStripePricesBasicTierPriceId = "PaymentSettings__Stripe__Prices__BasicTierPriceId";
        public const string PaymentSettingsStripePricesEssentialTierPriceId = "PaymentSettings__Stripe__Prices__EssentialTierPriceId";
        public const string PaymentSettingsStripePricesAdvancedTierPriceId = "PaymentSettings__Stripe__Prices__AdvancedTierPriceId";
        public const string PaymentSettingsStripePricesBasicTierYearlyPriceId = "PaymentSettings__Stripe__Prices__BasicTierYearlyPriceId";
        public const string PaymentSettingsStripePricesEssentialTierYearlyPriceId = "PaymentSettings__Stripe__Prices__EssentialTierYearlyPriceId";
        public const string PaymentSettingsStripePricesAdvancedTierYearlyPriceId = "PaymentSettings__Stripe__Prices__AdvancedTierYearlyPriceId";
        public const string AppSecret = "App__Secret";
        public const string TelegramSettingsBotToken = "TelegramSettings__BotToken";
        public const string TelegramSettingsClientId = "TelegramSettings__ClientId";
        public const string TelegramSettingsClientSecret = "TelegramSettings__ClientSecret";
        public const string DisableHttpsRedirection = "DisableHttpsRedirection";
        public const string EmailSenderEmail = "EmailSettings__SenderEmail";
        public const string EmailSenderName = "EmailSettings__SenderName";
        public const string EmailConnectionString = "EmailSettings__ConnectionString";
        public const string EmailBaseUrl = "EmailSettings__BaseUrl";
        public const string EmailRedirectUrl = "EmailSettings__RedirectUrl";
        public const string EmailLogoUrl = "EmailSettings__EmailLogoUrl";
        public const string EmailBlobStorageUrl = "EmailSettings__BlobStorageUrl";
        public const string EmailBlobStorageSasToken = "EmailSettings__BlobStorageSasToken";
        public const string HangfireDashboardAuthorizationUser0Username = "Hangfire__Dashboard__Authorization__Users__0__Username";
        public const string HangfireDashboardAuthorizationUser0Password = "Hangfire__Dashboard__Authorization__Users__0__Password";
        public const string HangfireIsFullAccessModeEnabled = "Hangfire__IsFullAccessModeEnabled";
        public const string JobSettingsMetterBillingJobStaleProcessingMaxAgeMinutes = "JobSettings__MetterBillingJob__StaleProcessingMaxAgeMinutes";
        public const string TripRadarApiApiKey = "TripRadarApi__ApiKey";
        public const string TripRadarApiBearerToken = "TripRadarApi__BearerToken";
        public const string KafkaBootstrapServers = "Kafka__BootstrapServers";
        public const string KafkaSaslMechanism = "Kafka__SaslMechanism";
        public const string KafkaSecurityProtocol = "Kafka__SecurityProtocol";
        public const string KafkaSaslUsername = "Kafka__SaslUsername";
        public const string KafkaSaslPassword = "Kafka__SaslPassword";
        public const string ElasticConfigurationUri = "ElasticConfiguration__Uri";
        public const string MockApiSerpApi = "MockApi__SerpApi";
        public const string ViteApiBaseUrl = "VITE_API_BASE_URL";
        public const string ViteApiKey = "VITE_API_KEY";
        public const string ViteStripePublishableKey = "VITE_STRIPE_PUBLISHABLE_KEY";
        public const string ViteAllowedHosts = "VITE_ALLOWED_HOSTS";
        public const string ViteDevHost = "VITE_DEV_HOST";
        public const string ViteDevPort = "VITE_DEV_PORT";
        public const string ViteDevHttps = "VITE_DEV_HTTPS";
        public const string ViteFirebaseApiKey = "VITE_FIREBASE_API_KEY";
        public const string ViteFirebaseAuthDomain = "VITE_FIREBASE_AUTH_DOMAIN";
        public const string ViteFirebaseProjectId = "VITE_FIREBASE_PROJECT_ID";
        public const string ViteFirebaseStorageBucket = "VITE_FIREBASE_STORAGE_BUCKET";
        public const string ViteFirebaseMessagingSenderId = "VITE_FIREBASE_MESSAGING_SENDER_ID";
        public const string ViteFirebaseAppId = "VITE_FIREBASE_APP_ID";
        public const string ViteFirebaseMeasurementId = "VITE_FIREBASE_MEASUREMENT_ID";
        public const string ViteTelegramBotUsername = "VITE_TELEGRAM_BOT_USERNAME";
        public const string ViteTelegramAuthBaseUrl = "VITE_TELEGRAM_AUTH_BASE_URL";
        public const string ViteTelegramClientId = "VITE_TELEGRAM_CLIENT_ID";
        public const string ViteTelemetryEnabled = "VITE_TELEMETRY_ENABLED";
        public const string ViteFrontendErrorIngestUrl = "VITE_FRONTEND_ERROR_INGEST_URL";
        public const string ViteAnalyticsDebug = "VITE_ANALYTICS_DEBUG";
        public const string ViteOtelEnabled = "VITE_OTEL_ENABLED";
        public const string ViteOtelServiceName = "VITE_OTEL_SERVICE_NAME";
        public const string ViteOtelEndpoint = "VITE_OTEL_ENDPOINT";
        public const string ViteOtelHeaders = "VITE_OTEL_HEADERS";
    }

    internal static class ConfigurationValues
    {
        public const string True = "true";
        public const string False = "false";
        public const string JwtIssuerTripRadar = "TripRadar";
        public const string JwtAudienceTripRadarUsers = "TripRadar.Users";
        public const string JwtDurationInMinutes = "1000";
        public const string JobSettingsMetterBillingJobStaleProcessingMaxAgeMinutes = "360";
        public const string StripeWebhookPath = "/api/v1.0/payments/webhook";
        public const string KafkaSecurityProtocolPlaintext = "Plaintext";
    }

    internal static class EnvironmentVariables
    {
        public const string EmailLogoUrl = "EMAIL_LOGO_URL";
        public const string CloudflareTunnelToken = "CLOUDFLARE_TUNNEL_TOKEN";
        public const string TelegramAuthBaseUrl = "TELEGRAM_AUTH_BASE_URL";
        public const string TelegramBotUsername = "TELEGRAM_BOT_USERNAME";
        public const string TelegramClientId = "TELEGRAM_CLIENT_ID";
        public const string TelegramClientSecret = "TELEGRAM_CLIENT_SECRET";
        public const string TelegramMiniAppUrl = "TELEGRAM_MINI_APP_URL";
        public const string TelegramWebhookUrl = "TELEGRAM_WEBHOOK_URL";
        public const string FirebaseApiKey = "FIREBASE_API_KEY";
        public const string FirebaseAuthDomain = "FIREBASE_AUTH_DOMAIN";
        public const string FirebaseProjectId = "FIREBASE_PROJECT_ID";
        public const string FirebaseStorageBucket = "FIREBASE_STORAGE_BUCKET";
        public const string FirebaseMessagingSenderId = "FIREBASE_MESSAGING_SENDER_ID";
        public const string FirebaseAppId = "FIREBASE_APP_ID";
        public const string FirebaseMeasurementId = "FIREBASE_MEASUREMENT_ID";
        public const string FrontendErrorIngestUrl = "FRONTEND_ERROR_INGEST_URL";
    }
}
