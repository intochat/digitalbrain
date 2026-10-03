using ClickHouse.Driver;
using DigitalBrain.Kernel;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using Orleans.Hosting;

namespace DigitalBrain.ClickHouse;

[ModuleDeployment("DigitalBrain.ClickHouse.ClickHouseDeployment, DigitalBrain.Modules.ClickHouse.Deployment")]
[ModuleId("clickhouse")]
public sealed class ClickHouseModule : IModule<ClickHouseModuleOptions>
{
    public const string DriverProviderName = "ClickHouse";

    public void Configure(ISiloBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        var services = builder.Services;
        services.AddOptions<ClickHouseModuleOptions>()
            .Configure<IConfiguration>(static (options, configuration) => configuration.PopulateModuleOptions("clickhouse", options))
            .PostConfigure<IConfiguration>(static (options, configuration) => options.ResolveConnection(configuration))
            .Validate(static options => string.Equals(options.Provider, DriverProviderName, StringComparison.OrdinalIgnoreCase),
                "ClickHouse requires the module option Provider=ClickHouse.")
            .Validate(static options => !string.IsNullOrWhiteSpace(options.ConnectionString),
                "ClickHouse requires a connection string for its configured connection name.");
        services.AddHttpClient(ClickHouseRegistration.HttpClientName, static client => client.Timeout = TimeSpan.FromMinutes(2));
        services.TryAddSingleton(services => new ClickHouseClient(services.GetRequiredService<IOptions<ClickHouseModuleOptions>>().Value.ConnectionString!,
            services.GetRequiredService<IHttpClientFactory>(), ClickHouseRegistration.HttpClientName));
        services.TryAddSingleton<IClickHouseProvider>(services => new ClickHouseDriverProvider(
            services.GetRequiredService<ClickHouseClient>(), ClickHouseDriverProvider.DatabaseOf(services.GetRequiredService<IOptions<ClickHouseModuleOptions>>().Value.ConnectionString!)));
        services.AddHealthChecks().AddCheck<ClickHouseHealthCheck>("clickhouse", tags: ["ready"]);
    }
}
