using DigitalBrain.Core;
using DigitalBrain.Core.Enforcement;
using DigitalBrain.Identity.Directory;
using DigitalBrain.Identity.Grants;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Orleans.Hosting;

namespace DigitalBrain.Identity;

[ModuleConfiguration(typeof(IdentityConfigurationContract))]
public sealed class IdentityModule : IModule
{
    public static ModuleDefinition Define() => new(typeof(IdentityModule));

    public void Configure(ISiloBuilder silo)
    {
        ArgumentNullException.ThrowIfNull(silo);
        var services = silo.Services;
        services.TryAddSingleton<IGrantPolicySource, GrainGrantPolicySource>();
        services.TryAddSingleton<IWorkspaceAccess, DirectoryWorkspaceAccess>();
        // The grant stage is the only enforcement increment in P2; allowances and limits add
        // further stages in P3.
        services.AddSingleton<ICallFilterStage, GrantCallFilterStage>();
    }

    public void Configure(IEndpointRouteBuilder endpoints)
    {
        ArgumentNullException.ThrowIfNull(endpoints);
        IdentityEndpoints.Map(endpoints);
    }
}
