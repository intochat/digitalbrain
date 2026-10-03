using DigitalBrain.Kernel;
using DigitalBrain.Kernel.Enforcement;
using DigitalBrain.Platform.Capacity;
using DigitalBrain.Platform.Contracts.Identity;
using DigitalBrain.Platform.Contracts.Integrations;
using DigitalBrain.Platform.Contracts.Integrations.Accounts;
using DigitalBrain.Platform.Identity;
using DigitalBrain.Platform.Identity.Directory;
using DigitalBrain.Platform.Identity.Grants;
using DigitalBrain.Platform.Integrations;
using DigitalBrain.Platform.Integrations.Accounts;
using DigitalBrain.Platform.Secrets;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Orleans.Hosting;

namespace DigitalBrain.Platform;

public static class PlatformHosting
{
    public static void AddDigitalBrainPlatform(this ISiloBuilder silo)
    {
        var services = silo.Services;
        if (services.Any(service => service.ServiceType == typeof(PlatformRegistration))) { return; }
        services.AddSingleton<PlatformRegistration>();
        services.AddIdentity();
        services.AddOptions<IdentityMigrationOptions>().BindConfiguration(IdentityMigrationOptions.SectionName);
        services.TryAddSingleton<DeploymentStorageIdentity>();
        silo.AddStartupTask<IdentityMigrationStartup>();
        services.TryAddSingleton<DigitalBrain.Platform.Contracts.Auth.ITokenHandoff, Auth.TokenHandoff>();
        services.TryAddSingleton<DigitalBrain.Platform.Contracts.Auth.IOAuthCredentials, Auth.OAuthCredentials>();
        services.AddMasterKeyWrapper();
        services.TryAddSingleton<IReadOnlyList<IntegrationDefinition>>(provider =>
            IntegrationDiscovery.Collect(provider.GetRequiredService<ModuleInventory>().Types));
        services.TryAddSingleton<IAccountProbe, CredentialPresenceProbe>();
        services.TryAddSingleton<ConnectionCredentialProbe>();
        services.TryAddSingleton<RegistrationCredentials>();
        services.TryAddSingleton<ICapabilities, Capabilities>();
        silo.AddStartupTask<RegistrationSeeder>();
        services.AddCapacity();
        services.TryAddSingleton<IGrantPolicySource, GrainGrantPolicySource>();
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
