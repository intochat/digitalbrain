using DigitalBrain.Sdk.Integrations.Accounts;
using DigitalBrain.Core;
using DigitalBrain.Core.Enforcement;
using DigitalBrain.Identity;
using DigitalBrain.Identity.Directory;
using DigitalBrain.Identity.Grants;
using DigitalBrain.Platform.Capacity;
using DigitalBrain.Platform.Integrations;
using DigitalBrain.Platform.Integrations.Accounts;
using DigitalBrain.Platform.Secrets;
using DigitalBrain.Sdk.Identity;
using DigitalBrain.Sdk.Integrations;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Orleans.Hosting;

namespace DigitalBrain.Platform;

public static class PlatformHosting
{
    internal static void AddPlatform(this ISiloBuilder silo)
    {
        var services = silo.Services;
        if (services.Any(service => service.ServiceType == typeof(PlatformRegistration))) { return; }
        services.AddSingleton<PlatformRegistration>();
        services.TryAddSingleton<IKeyVault, FakeKeyVault>();
        if (OperatingSystem.IsWindows()) { services.TryAddSingleton<IKeyWrapper, DpapiKeyWrapper>(); }
        else { services.TryAddSingleton<IKeyWrapper, KeyVaultKeyWrapper>(); }
        services.TryAddSingleton<IReadOnlyList<IntegrationDefinition>>(provider =>
            IntegrationDiscovery.Collect(provider.GetRequiredService<ModuleInventory>().Types));
        services.TryAddSingleton<IAccountProbe, CredentialPresenceProbe>();
        services.TryAddSingleton<ICapabilities, Capabilities>();
        silo.AddStartupTask<RegistrationSeeder>();
        services.AddCapacity();
        services.TryAddSingleton<IGrantPolicySource, GrainGrantPolicySource>();
        services.TryAddSingleton<IBrainAccess, DirectoryBrainAccess>();
        services.TryAddSingleton<IIdentity, PlatformIdentity>();
        services.TryAddEnumerable(ServiceDescriptor.Singleton<ICallFilterStage, GrantCallFilterStage>());
    }

    public static IEndpointRouteBuilder MapDigitalBrainPlatform(this IEndpointRouteBuilder endpoints)
    {
        ArgumentNullException.ThrowIfNull(endpoints);
        endpoints.MapSecrets();
        endpoints.MapIntegrations();
        endpoints.MapAccounts();
        endpoints.MapCapabilities();
        IdentityEndpoints.Map(endpoints);
        return endpoints;
    }

    private sealed class PlatformRegistration;
}
