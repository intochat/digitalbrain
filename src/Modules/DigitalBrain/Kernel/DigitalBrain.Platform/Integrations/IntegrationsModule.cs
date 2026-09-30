using DigitalBrain.Core;
using DigitalBrain.Sdk.Integrations.Accounts;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Orleans.Hosting;

namespace DigitalBrain.Sdk.Integrations;

public sealed class IntegrationsModule : IModule
{
    public void Configure(ISiloBuilder silo)
    {
        ArgumentNullException.ThrowIfNull(silo);
        // Every composed module's declared integrations, collected once from the host's module inventory.
        silo.Services.AddSingleton<IReadOnlyList<IntegrationDefinition>>(services =>
            IntegrationDiscovery.Collect(services.GetRequiredService<ModuleInventory>().Types));
        silo.Services.TryAddSingleton(TimeProvider.System);
        silo.Services.TryAddSingleton<IAccountProbe, CredentialPresenceProbe>();
        silo.Services.TryAddSingleton<ICapabilities, Capabilities>();
        silo.AddStartupTask<RegistrationSeeder>();
    }

    public void Configure(IEndpointRouteBuilder endpoints)
    {
        ArgumentNullException.ThrowIfNull(endpoints);
        endpoints.MapIntegrations();
        endpoints.MapAccounts();
        endpoints.MapCapabilities();
    }
}
