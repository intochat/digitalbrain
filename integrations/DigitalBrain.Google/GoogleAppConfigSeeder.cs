using DigitalBrain.Core.Config;
using DigitalBrain.Google;
using DigitalBrain.Kernel.Abstractions;
using DigitalBrain.Kernel.Capabilities;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace DigitalBrain.Google;

public static class GoogleServiceCollectionExtensions
{
    public static IServiceCollection AddDigitalBrainGoogle(this IServiceCollection services)
    {
        services.AddHostedService<GoogleAppConfigSeeder>();
        services.AddSingleton<IGmailApiClientFactory, GmailApiClientFactory>();
        services.AddSingleton<ICapabilityHandler, GmailMailboxCapabilityHandler>();
        services.AddSingleton<ICapabilityHandler, GmailSendProposalCapabilityHandler>();
        services.AddSingleton<IInoEffectHandler, GmailSendEffectHandler>();
        services.AddKeyedSingleton<IConnector>("google", (provider, _) => new GoogleConnector(
            provider.GetRequiredService<IPackConfigStore>(),
            provider.GetRequiredService<IOAuthStateProtector>(),
            provider.GetService<IConfiguration>()));
        return services;
    }
}

public sealed class GoogleAppConfigSeeder(
    IConfiguration configuration,
    IPackConfigStore store,
    ILogger<GoogleAppConfigSeeder> logger)
    : IHostedService
{
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        var appConfig = GoogleAppConfig.From(configuration);
        if (!appConfig.HasAnyValue)
        {
            return;
        }

        if (!appConfig.HasConnectedAppConfig)
        {
            logger.LogWarning("Google app-level configuration is incomplete.");
            return;
        }

        var existing = await store.GetAsync(GoogleClientFactory.DefaultScope, GoogleClientFactory.PackName, cancellationToken).ConfigureAwait(false);
        var merged = new Dictionary<string, string>(existing, StringComparer.OrdinalIgnoreCase);

        var changed = SetIfConfigured(merged, GoogleClientFactory.ClientIdKey, appConfig.ClientId);
        changed |= SetIfConfigured(merged, GoogleClientFactory.ClientSecretKey, appConfig.ClientSecret);
        changed |= SetIfConfigured(merged, GoogleClientFactory.RedirectUriKey, appConfig.RedirectUri);

        if (!changed)
        {
            return;
        }

        await store.SetAsync(GoogleClientFactory.DefaultScope, GoogleClientFactory.PackName, merged, cancellationToken).ConfigureAwait(false);
        logger.LogInformation("Seeded Google OAuth client configuration.");
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    private static bool SetIfConfigured(IDictionary<string, string> values, string key, string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        var trimmed = value.Trim();
        if (values.TryGetValue(key, out var existing) && string.Equals(existing, trimmed, StringComparison.Ordinal))
        {
            return false;
        }

        values[key] = trimmed;
        return true;
    }

    private sealed record GoogleAppConfig(string? ClientId, string? ClientSecret, string? RedirectUri)
    {
        public bool HasAnyValue => HasValue(ClientId) || HasValue(ClientSecret) || HasValue(RedirectUri);
        public bool HasConnectedAppConfig => HasValue(ClientId) && HasValue(ClientSecret);
        public static GoogleAppConfig From(IConfiguration configuration) => new(
            configuration["DigitalBrain:Google:ClientId"],
            configuration["DigitalBrain:Google:ClientSecret"],
            configuration["DigitalBrain:Google:RedirectUri"]);
        private static bool HasValue(string? value) => !string.IsNullOrWhiteSpace(value);
    }
}
