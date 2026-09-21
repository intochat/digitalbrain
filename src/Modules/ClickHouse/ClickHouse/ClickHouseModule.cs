using ClickHouse.Driver;
using DigitalBrain.Core;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using Orleans.Hosting;

namespace DigitalBrain.ClickHouse;

[ModuleConfiguration(typeof(ClickHouseConfigurationContract))]
[ModuleHosting("DigitalBrain.ClickHouse.Aspire.Hosting.ClickHouseModuleHosting, DigitalBrain.Modules.ClickHouse.Aspire.Hosting")]
public sealed class ClickHouseModule : IModule
{
    public const string ConfigurationRoot = ClickHouseModuleOptions.SectionName;
    public const string ProviderConfigurationKey = ClickHouseModuleOptions.SectionName + ":Provider";
    public const string DriverProviderName = "ClickHouse";

    public static ModuleDefinition Define(ClickHouseModuleOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        return new(typeof(ClickHouseModule), new Dictionary<string, string?>
        {
            [ClickHouseModuleOptions.SectionName + ":Provider"] = options.Provider,
            [ClickHouseModuleOptions.SectionName + ":ConnectionName"] = options.ConnectionName,
        });
    }

    public void Configure(ISiloBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        var services = builder.Services;
        services.AddOptions<ClickHouseModuleOptions>()
            .BindConfiguration(ClickHouseModuleOptions.SectionName)
            .PostConfigure<IConfiguration>(static (options, configuration) => options.ResolveConnection(configuration))
            .Validate(static options => string.Equals(options.Provider, DriverProviderName, StringComparison.OrdinalIgnoreCase),
                "ClickHouse requires DigitalBrain:ClickHouse:Provider=ClickHouse.")
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