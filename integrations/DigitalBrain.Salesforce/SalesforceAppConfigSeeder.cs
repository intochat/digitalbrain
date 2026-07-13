using DigitalBrain.Core.Config;
using DigitalBrain.Kernel.Abstractions;
using DigitalBrain.Salesforce;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace DigitalBrain.Salesforce;

public static class SalesforceServiceCollectionExtensions
{
    public static IServiceCollection AddDigitalBrainSalesforce(this IServiceCollection services)
    {
        services.AddHostedService<SalesforceAppConfigSeeder>();
        services.AddSingleton<ISalesforceApiClientFactory, SalesforceApiClientFactory>();
        services.AddKeyedSingleton<IConnector>("salesforce", (provider, _) => new SalesforceConnector(
            provider.GetRequiredService<ISalesforceApiClientFactory>(),
            provider.GetRequiredService<IPackConfigStore>(),
            provider.GetRequiredService<IOAuthStateProtector>()));
        return services;
    }
}

public sealed class SalesforceAppConfigSeeder(
    IConfiguration configuration,
    IPackConfigStore store,
    ILogger<SalesforceAppConfigSeeder> logger)
    : IHostedService
{
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        var appConfig = SalesforceAppConfig.From(configuration);
        if (!appConfig.HasAnyValue)
        {
            return;
        }

        if (!appConfig.HasConnectedAppConfig)
        {
            logger.LogWarning(
                "Salesforce app-level configuration is incomplete. Configure DigitalBrain:Salesforce:ClientId and DigitalBrain:Salesforce:ClientSecret to enable Login via Salesforce.");
            return;
        }

        var existing = await store
            .GetAsync(SalesforceClientFactory.DefaultScope, SalesforceClientFactory.PackName, cancellationToken)
            .ConfigureAwait(false);
        var merged = new Dictionary<string, string>(existing, StringComparer.OrdinalIgnoreCase);

        var changed = SetIfConfigured(merged, SalesforceClientFactory.ClientIdKey, appConfig.ClientId);
        changed |= SetIfConfigured(merged, SalesforceClientFactory.ClientSecretKey, appConfig.ClientSecret);
        changed |= SetIfConfigured(
            merged,
            SalesforceClientFactory.LoginUrlKey,
            string.IsNullOrWhiteSpace(appConfig.LoginUrl)
                ? SalesforceClientFactory.DefaultLoginUrl
                : appConfig.LoginUrl);
        changed |= SetIfConfigured(
            merged,
            SalesforceClientFactory.ApiVersionKey,
            string.IsNullOrWhiteSpace(appConfig.ApiVersion)
                ? SalesforceClientFactory.DefaultApiVersion
                : appConfig.ApiVersion);
        changed |= SetIfConfigured(merged, SalesforceClientFactory.RedirectUriKey, appConfig.RedirectUri);

        if (!changed)
        {
            return;
        }

        await store
            .SetAsync(SalesforceClientFactory.DefaultScope, SalesforceClientFactory.PackName, merged, cancellationToken)
            .ConfigureAwait(false);

        logger.LogInformation("Seeded Salesforce Connected App configuration from host configuration.");
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    private static bool SetIfConfigured(IDictionary<string, string> values, string key, string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        var trimmed = value.Trim();
        if (values.TryGetValue(key, out var existing) &&
            string.Equals(existing, trimmed, StringComparison.Ordinal))
        {
            return false;
        }

        values[key] = trimmed;
        return true;
    }

    private sealed record SalesforceAppConfig(
        string? ClientId,
        string? ClientSecret,
        string? LoginUrl,
        string? ApiVersion,
        string? RedirectUri)
    {
        public bool HasAnyValue =>
            HasValue(ClientId) || HasValue(ClientSecret) || HasValue(LoginUrl) || HasValue(ApiVersion) || HasValue(RedirectUri);

        public bool HasConnectedAppConfig => HasValue(ClientId) && HasValue(ClientSecret);

        public static SalesforceAppConfig From(IConfiguration configuration) => new(
            configuration["DigitalBrain:Salesforce:ClientId"],
            configuration["DigitalBrain:Salesforce:ClientSecret"],
            configuration["DigitalBrain:Salesforce:LoginUrl"],
            configuration["DigitalBrain:Salesforce:ApiVersion"],
            configuration["DigitalBrain:Salesforce:RedirectUri"]);

        private static bool HasValue(string? value) => !string.IsNullOrWhiteSpace(value);
    }
}
