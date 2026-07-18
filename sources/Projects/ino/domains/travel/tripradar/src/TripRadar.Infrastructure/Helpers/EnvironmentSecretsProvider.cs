namespace TripRadar.Infrastructure.Helpers;

using System.Text.RegularExpressions;
using Constants;
using Npgsql;

/// <summary>
/// Helper class for extracting secrets from environment variables for KeyVault configuration.
/// Used in CI/CD when .env file is not available - secrets come from GitHub Secrets.
/// </summary>
public static class EnvironmentSecretsProvider
{
    public static Dictionary<string, string> GetKeyVaultSecretsFromEnvironment(string dbPassword)
    {
        var envVars = GetSecretsFromEnvironment(dbPassword);
        var secrets = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        foreach (var kv in envVars)
        {
            var keyVaultName = ToKeyVaultSecretName(kv.Key);
            if (!string.IsNullOrWhiteSpace(keyVaultName))
            {
                secrets[keyVaultName] = kv.Value;
            }
        }

        return secrets;
    }

    /// <summary>
    /// Reads secrets from environment variables (set from GitHub Secrets in CI/CD)
    /// and returns them as configuration keys (double underscore format).
    /// </summary>
    /// <param name="dbPassword">Database password for building connection string</param>
    /// <returns>Dictionary of secret names and values</returns>
    private static Dictionary<string, string> GetSecretsFromEnvironment(string dbPassword)
    {
        var secrets = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        var postgresServer = GetEnv("POSTGRES_SERVER_FQDN");
        var postgresDatabase = GetEnv("POSTGRES_DB_NAME") ?? GetEnv("POSTGRES_DATABASE_NAME");

        if (!string.IsNullOrWhiteSpace(postgresServer) &&
            !string.IsNullOrWhiteSpace(postgresDatabase) &&
            !string.IsNullOrWhiteSpace(dbPassword))
        {
            var builder = new NpgsqlConnectionStringBuilder
            {
                Host = postgresServer,
                Port = 5432,
                Database = postgresDatabase,
                Username = TripRadarServerConstants.AdminUser,
                Password = dbPassword,
                SslMode = SslMode.Require
            };

            AddSecret(secrets, "ConnectionStrings__AppDb", builder.ToString());
        }

        // Application Settings
        AddSecret(secrets, "CorsOriginsWhiteList", GetEnv("CORS_ORIGINS"));
        AddSecret(secrets, "EmailSettings__BaseUrl", GetEnv("EMAIL_BASE_URL"));
        AddSecret(secrets, "EmailSettings__RedirectUrl", GetEnv("REDIRECT_URL"));
        AddSecret(secrets, "EmailSettings__LogoUrl", GetEnv("EMAIL_LOGO_URL"));
        AddSecret(secrets, "EmailSettings__EmailLogoUrl", GetEnv("EMAIL_LOGO_URL"));

        // JWT and Authentication
        AddSecret(secrets, "Jwt__Key", GetEnv("JWT_KEY"));
        AddSecret(secrets, "Jwt__RefreshTokenKey", GetEnv("JWT_REFRESH_TOKEN_KEY"));
        AddSecret(secrets, "ApiKey", GetEnv("API_KEY"));
        AddSecret(secrets, "InternalApiKey", GetEnv("INTERNAL_API_KEY"));
        AddSecret(secrets, "HANGFIRE_DASHBOARD_KEY", GetEnv("HANGFIRE_DASHBOARD_KEY"));
        AddSecret(secrets, "App__Secret", GetEnv("APP_SECRET"));
        AddSecret(secrets, "Encryption__UserDataKey", GetEnv("ENCRYPTION_USER_DATA_KEY"));
        AddSecret(secrets, "Jaeger__Endpoint", GetEnv("JAEGER_ENDPOINT"));

        // Google OAuth
        AddSecret(secrets, "GoogleAuth__ClientId", GetEnv("GOOGLE_CLIENT_ID"));
        AddSecret(secrets, "GoogleAuth__ClientSecret", GetEnv("GOOGLE_CLIENT_SECRET"));

        // Telegram
        AddSecret(secrets, "TelegramSettings__BotToken", GetEnv("TELEGRAM_BOT_TOKEN"));
        AddSecret(secrets, "TelegramSettings__ClientId", GetEnv("TELEGRAM_CLIENT_ID"));
        AddSecret(secrets, "TelegramSettings__ClientSecret", GetEnv("TELEGRAM_CLIENT_SECRET"));

        // Stripe Payment Settings
        AddSecret(secrets, "PaymentSettings__Stripe__SecretKey", GetEnv("STRIPE_SECRET_KEY"));
        AddSecret(secrets, "PaymentSettings__Stripe__PublishableKey", GetEnv("STRIPE_PUBLISHABLE_KEY"));
        AddSecret(secrets, "PaymentSettings__Stripe__WebhookSecret", GetEnv("STRIPE_WEBHOOK_SECRET"));
        AddSecret(secrets, "PaymentSettings__Stripe__BasicTierPriceId", GetEnv("STRIPE_BASIC_TIER_PRICE_ID"));
        AddSecret(secrets, "PaymentSettings__Stripe__EssentialTierPriceId", GetEnv("STRIPE_ESSENTIAL_TIER_PRICE_ID"));
        AddSecret(secrets, "PaymentSettings__Stripe__AdvancedTierPriceId", GetEnv("STRIPE_ADVANCED_TIER_PRICE_ID"));
        AddSecret(secrets, "PaymentSettings__Stripe__BasicTierYearlyPriceId", GetEnv("STRIPE_BASIC_TIER_YEARLY_PRICE_ID"));
        AddSecret(secrets, "PaymentSettings__Stripe__EssentialTierYearlyPriceId", GetEnv("STRIPE_ESSENTIAL_TIER_YEARLY_PRICE_ID"));
        AddSecret(secrets, "PaymentSettings__Stripe__AdvancedTierYearlyPriceId", GetEnv("STRIPE_ADVANCED_TIER_YEARLY_PRICE_ID"));
        AddSecret(secrets, "PaymentSettings__Stripe__SuccessUrl", GetEnv("STRIPE_SUCCESS_URL"));
        AddSecret(secrets, "PaymentSettings__Stripe__CancelUrl", GetEnv("STRIPE_CANCEL_URL"));

        // External Provider API Keys
        AddSecret(secrets, "SerpApiSettings__ApiKey", GetEnv("SERP_API_KEY"));

        // Email Settings (Azure Communication Services)
        AddSecret(secrets, "EmailSettings__ConnectionString", GetEnv("EMAIL_CONNECTION_STRING"));
        AddSecret(secrets, "EmailSettings__SenderEmail", GetEnv("EMAIL_SENDER_EMAIL"));
        AddSecret(secrets, "EmailSettings__SenderName", GetEnv("EMAIL_SENDER_NAME"));

        // Azure Blob Storage
        AddSecret(secrets, "EmailSettings__BlobStorageUrl", GetEnv("BLOB_STORAGE_URL"));
        AddSecret(secrets, "EmailSettings__BlobStorageSasToken", GetEnv("BLOB_STORAGE_SAS_TOKEN"));

        // Azure Front Door
        AddSecret(secrets, "Azure__FrontDoorId", GetEnv("AZURE_FRONTDOOR_ID"));

        // Observability (optional - for non-production environments)
        AddSecret(secrets, "ElasticConfiguration__Uri", GetEnv("ELASTICSEARCH_URI"));
        AddSecret(secrets, "Otel__ExporterOtlpEndpoint", GetEnv("OTEL_EXPORTER_OTLP_ENDPOINT"));

        // Kafka / Event Hubs
        AddSecret(secrets, "Kafka__BootstrapServers", GetEnv("KAFKA_BOOTSTRAP_SERVERS"));
        AddSecret(secrets, "Kafka__ConnectionString", GetEnv("KAFKA_CONNECTION_STRING"));

        return secrets;
    }

    private static string? GetEnv(string name) => Environment.GetEnvironmentVariable(name);

    private static void AddSecret(Dictionary<string, string> secrets, string key, string? value)
    {
        if (!string.IsNullOrWhiteSpace(value))
        {
            secrets[key] = value;
        }
    }

    private static string ToKeyVaultSecretName(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return string.Empty;
        }

        var transformed = name.Replace('_', '-');
        transformed = Regex.Replace(transformed, "[^a-zA-Z0-9-]", string.Empty);
        return transformed.Trim('-');
    }
}
