using ClickHouse.Driver;
using DigitalBrain.AI;
using DigitalBrain.Core;
using DigitalBrain.UI;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace DigitalBrain.ClickHouse;

public sealed class ClickHouseModule : IModule
{
    public const string ConfigurationRoot = "DigitalBrain:ClickHouse";
    public const string ProviderConfigurationKey = "DigitalBrain:ClickHouse:Provider";
    public const string DriverProviderName = "ClickHouse";

    public void Configure(ISiloBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        var services = builder.Services;
        var configuration = builder.Configuration;
        var provider = configuration[ProviderConfigurationKey];
        var connectionName = ResolveConnectionName(configuration);
        var connectionString = configuration.GetConnectionString(connectionName) ?? configuration[$"ConnectionStrings:{connectionName}"];

        if (DigitalBrainFakes.Enabled(configuration) || string.IsNullOrWhiteSpace(provider))
        {
            if (!DigitalBrainFakes.Enabled(configuration) && !string.IsNullOrWhiteSpace(connectionString))
            {
                throw new InvalidOperationException(
                    $"Connection string '{connectionName}' is configured but '{ProviderConfigurationKey}' is unset; ClickHouse would " +
                    $"silently answer from an in-memory fake. Set '{ProviderConfigurationKey}' to '{DriverProviderName}' to use it, " +
                    $"or remove connection string '{connectionName}' to genuinely opt into the fake.");
            }

            services.TryAddSingleton<FakeClickHouseProvider>();
            services.TryAddSingleton<IClickHouseProvider>(static services => services.GetRequiredService<FakeClickHouseProvider>());
        }
        else if (string.Equals(provider, DriverProviderName, StringComparison.OrdinalIgnoreCase))
        {
            if (string.IsNullOrWhiteSpace(connectionString))
            {
                throw new InvalidOperationException($"ClickHouse provider '{DriverProviderName}' requires connection string '{connectionName}'.");
            }

            services.AddHttpClient(ClickHouseRegistration.HttpClientName, static client => client.Timeout = TimeSpan.FromMinutes(2));
            services.TryAddSingleton(services => new ClickHouseClient(connectionString, services.GetRequiredService<IHttpClientFactory>(), ClickHouseRegistration.HttpClientName));
            services.TryAddSingleton<IClickHouseProvider>(services => new ClickHouseDriverProvider(
                services.GetRequiredService<ClickHouseClient>(), ClickHouseDriverProvider.DatabaseOf(connectionString)));
            services.AddHealthChecks().AddCheck<ClickHouseHealthCheck>("clickhouse", tags: ["ready"]);
        }
        else
        {
            throw new InvalidOperationException($"'{ProviderConfigurationKey}' is '{provider}'; the only supported provider is '{DriverProviderName}' (or unset for the fake).");
        }

        services.AddSingleton<ITableSource>(new TableSource(ClickHouseNames.TableIdPrefix, ClickHouseNames.TableType));
        // The tools need TableService whether or not the UI module is composed; both register it TryAdd-style.
        services.TryAddSingleton<TableService>();
        services.AddSingleton<ClickHouseNativeTools>();
        services.AddNativeTool("clickhouse_schema", static services => services.GetRequiredService<ClickHouseNativeTools>().CreateSchema());
        services.AddNativeTool("clickhouse_query", static services => services.GetRequiredService<ClickHouseNativeTools>().CreateQuery());
        services.AddNativeTool("show_query_table", static services => services.GetRequiredService<ClickHouseNativeTools>().CreateShowQueryTable());
    }

    private static string ResolveConnectionName(IConfiguration configuration)
    {
        var connectionName = configuration[ClickHouseRegistration.ConnectionNameConfigurationKey];
        return string.IsNullOrWhiteSpace(connectionName) ? ClickHouseRegistration.DefaultConnectionName : connectionName;
    }
}
