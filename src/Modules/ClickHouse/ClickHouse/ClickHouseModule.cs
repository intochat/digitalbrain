using ClickHouse.Driver;
using DigitalBrain.AI;
using DigitalBrain.Core;
using DigitalBrain.Flutter;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;

namespace DigitalBrain.ClickHouse;

public sealed class ClickHouseModule : IModule
{
    public const string ConfigurationRoot = ClickHouseOptions.SectionName;
    public const string ProviderConfigurationKey = "DigitalBrain:ClickHouse:Provider";
    public const string DriverProviderName = "ClickHouse";

    public void Configure(ISiloBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        var services = builder.Services;
        services.AddOptions<ClickHouseOptions>()
            .BindConfiguration(ConfigurationRoot)
            .PostConfigure<IConfiguration>(static (options, configuration) => options.ResolveConnection(configuration))
            .Validate(static options => string.Equals(options.Provider, DriverProviderName, StringComparison.OrdinalIgnoreCase),
                "ClickHouse requires DigitalBrain:ClickHouse:Provider=ClickHouse.")
            .Validate(static options => !string.IsNullOrWhiteSpace(options.ConnectionString),
                "ClickHouse requires a connection string for its configured connection name.")
            .ValidateOnStart();
        services.AddHttpClient(ClickHouseRegistration.HttpClientName, static client => client.Timeout = TimeSpan.FromMinutes(2));
        services.TryAddSingleton(services => new ClickHouseClient(services.GetRequiredService<IOptions<ClickHouseOptions>>().Value.ConnectionString!, services.GetRequiredService<IHttpClientFactory>(), ClickHouseRegistration.HttpClientName));
        services.TryAddSingleton<IClickHouseProvider>(services => new ClickHouseDriverProvider(
            services.GetRequiredService<ClickHouseClient>(), ClickHouseDriverProvider.DatabaseOf(services.GetRequiredService<IOptions<ClickHouseOptions>>().Value.ConnectionString!)));
        services.AddHealthChecks().AddCheck<ClickHouseHealthCheck>("clickhouse", tags: ["ready"]);

        services.AddSingleton<ITableSource>(new TableSource(ClickHouseNames.TableIdPrefix, ClickHouseNames.TableType));
        // The tools need TableService whether or not the UI module is composed; both register it TryAdd-style.
        services.TryAddSingleton<TableService>();
        services.AddSingleton<ClickHouseNativeTools>();
        services.AddNativeTool("clickhouse_schema", static services => services.GetRequiredService<ClickHouseNativeTools>().CreateSchema());
        services.AddNativeTool("clickhouse_query", static services => services.GetRequiredService<ClickHouseNativeTools>().CreateQuery());
        services.AddNativeTool("show_query_table", static services => services.GetRequiredService<ClickHouseNativeTools>().CreateShowQueryTable());
    }
}
