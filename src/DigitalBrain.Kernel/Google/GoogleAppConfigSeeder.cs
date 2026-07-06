using DigitalBrain.Core.Config;
using DigitalBrain.Google;
using Microsoft.Extensions.Logging;

namespace DigitalBrain.Kernel.Google;

internal sealed class GoogleAppConfigSeeder(
    IConfiguration configuration,
    IPackConfigStore store,
    ILogger<GoogleAppConfigSeeder> logger)
    : IHostedService
{
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        var appConfig = GoogleAppConfig.From(configuration);
        if (!appConfig.HasAnyValue)
            return;

        if (!appConfig.HasConnectedAppConfig)
        {
            logger.LogWarning(
                "Google app-level configuration is incomplete. Configure DigitalBrain:Google:ClientId and DigitalBrain:Google:ClientSecret (or use aspire parameters google-client-id / google-client-secret) to enable Sign in with Google.");
            return;
        }

        var existing = await store
            .GetAsync(GoogleClientFactory.DefaultScope, GoogleClientFactory.PackName)
            .ConfigureAwait(false);
        var merged = new Dictionary<string, string>(existing, StringComparer.OrdinalIgnoreCase);

        var changed = SetIfConfigured(merged, GoogleClientFactory.ClientIdKey, appConfig.ClientId);
        changed |= SetIfConfigured(merged, GoogleClientFactory.ClientSecretKey, appConfig.ClientSecret);
        changed |= SetIfConfigured(merged, GoogleClientFactory.RedirectUriKey, appConfig.RedirectUri);

        if (!changed)
            return;

        await store
            .SetAsync(GoogleClientFactory.DefaultScope, GoogleClientFactory.PackName, merged)
            .ConfigureAwait(false);

        logger.LogInformation("Seeded Google OAuth client configuration from host configuration.");
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    private static bool SetIfConfigured(IDictionary<string, string> values, string key, string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return false;

        var trimmed = value.Trim();
        if (values.TryGetValue(key, out var existing) &&
            string.Equals(existing, trimmed, StringComparison.Ordinal))
        {
            return false;
        }

        values[key] = trimmed;
        return true;
    }

    private sealed record GoogleAppConfig(
        string? ClientId,
        string? ClientSecret,
        string? RedirectUri)
    {
        public bool HasAnyValue =>
            HasValue(ClientId) || HasValue(ClientSecret) || HasValue(RedirectUri);

        public bool HasConnectedAppConfig => HasValue(ClientId) && HasValue(ClientSecret);

        public static GoogleAppConfig From(IConfiguration configuration) => new(
            configuration["DigitalBrain:Google:ClientId"],
            configuration["DigitalBrain:Google:ClientSecret"],
            configuration["DigitalBrain:Google:RedirectUri"]);

        private static bool HasValue(string? value) => !string.IsNullOrWhiteSpace(value);
    }
}