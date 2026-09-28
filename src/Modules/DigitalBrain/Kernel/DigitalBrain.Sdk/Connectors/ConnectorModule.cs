using DigitalBrain.Core;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Orleans.Hosting;

namespace DigitalBrain.Sdk.Connectors;

[ModuleConfiguration(typeof(ConnectorConfigurationContract))]
public sealed class ConnectorModule : IModule
{
    public static ModuleDefinition Define() => new(typeof(ConnectorModule));

    public void Configure(ISiloBuilder silo)
    {
        ArgumentNullException.ThrowIfNull(silo);
        silo.Services.TryAddSingleton(TimeProvider.System);
        silo.Services.TryAddSingleton<IConnectorProbe, CredentialPresenceProbe>();
    }

    public void Configure(IEndpointRouteBuilder endpoints)
    {
        ArgumentNullException.ThrowIfNull(endpoints);
        endpoints.MapConnectors();
    }
}

public sealed class ConnectorModuleOptions;

public sealed class ConnectorConfigurationContract() : ModuleConfigurationContract<ConnectorModule, ConnectorModuleOptions>
{
    protected override ModuleDefinition Compile(ConnectorModuleOptions options) => ConnectorModule.Define();
}

