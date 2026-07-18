using System.Net;
using Ino.Core;
using Ino.Core.Hosting.Brain;
using Ino.Core.Hosting.Placement;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Orleans.Hosting;
using Orleans.Runtime.MembershipService.SiloMetadata;

namespace Ino.Core.Hosting;

public static class DomainHostingExtensions
{
    /// <summary>
    /// Boots a single-domain silo. The silo connects to the kernel silo as
    /// Orleans primary, tags itself with <see cref="IDomain.Id"/> so PinToSilo
    /// placement can route synapses to it, and registers its declared neurons
    /// with the cluster-wide Discovery grain.
    ///
    /// Each domain gets its own silo process / Aspire resource so OTel emits
    /// per-domain <c>service.name</c> for cross-domain trace filtering and
    /// the dashboard surfaces install/uninstall as separate resource lifecycles.
    /// </summary>
    public static IHostApplicationBuilder AddDomain(
        this IHostApplicationBuilder builder,
        IDomain domain,
        int defaultSiloPort,
        int defaultGatewayPort)
    {
        var siloPort = InoOrleansEndpoints.DomainSiloPort(domain.Id, defaultSiloPort);
        var gatewayPort = InoOrleansEndpoints.DomainGatewayPort(domain.Id, defaultGatewayPort);

        builder.UseOrleans(silo =>
        {
            silo.UseLocalhostClustering(
                siloPort: siloPort,
                gatewayPort: gatewayPort,
                primarySiloEndpoint: new IPEndPoint(IPAddress.Loopback, InoOrleansEndpoints.KernelSiloPort),
                serviceId: InoOrleansEndpoints.ServiceId,
                clusterId: InoOrleansEndpoints.ClusterId);

            silo.UseSiloMetadata(new Dictionary<string, string>
            {
                [PinToSiloStrategy.SiloMetadataKey] = domain.Id.Value,
            });

            silo.UseInoJournaling();
            silo.UseInoBrainStream();
        });

        builder.Services.AddPinToSiloPlacement();
        builder.Services.AddSingleton(domain);
        // Touch the assembly so Orleans scans it for grain types.
        _ = domain.GetType().Assembly;

        builder.Services.Configure<RegistrationOptions>(o =>
        {
            o.Silo = domain.Id;
            o.Domains = [domain];
        });
        builder.Services.AddHostedService<RegistrationHostedService>();

        builder.Services.AddSingleton<IDiscoveryClient, DiscoveryClient>();

        builder.Services.AddSingleton<ICapabilityEnforcer>(_ =>
            new CapabilityEnforcer(new Dictionary<DomainId, IReadOnlyList<Capability>>
            {
                [domain.Id] = domain.DeclaredCapabilities.ToArray(),
            }));

        builder.Services.AddSingleton(
            _ => new global::System.Diagnostics.ActivitySource(Telemetry.ActivitySourceName));
        builder.Services.AddSingleton<IFirePort, FirePort>();

        builder.Services.AddSingleton<IAmbientFire>(sp => new AmbientFire(
            sp.GetRequiredService<IFirePort>(),
            domain.Id,
            sp.GetRequiredService<ILogger<AmbientFire>>()));

        return builder;
    }
}
