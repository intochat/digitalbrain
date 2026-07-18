using System.Net;
using Ino.Core;
using Ino.Core.Hosting;
using Ino.Core.Hosting.Brain;
using Ino.Core.Hosting.Placement;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Orleans.Hosting;
using Orleans.Runtime.MembershipService.SiloMetadata;

namespace Ino.Identity;

public static class IdentitySiloConfigurator
{
    public static IHostApplicationBuilder AddIdentity(this IHostApplicationBuilder builder)
    {
        builder.UseOrleans(silo =>
        {
            silo.UseLocalhostClustering(
                siloPort: InoOrleansEndpoints.IdentitySiloPort,
                gatewayPort: InoOrleansEndpoints.IdentityGatewayPort,
                primarySiloEndpoint: new IPEndPoint(IPAddress.Loopback, InoOrleansEndpoints.KernelSiloPort),
                serviceId: InoOrleansEndpoints.ServiceId,
                clusterId: InoOrleansEndpoints.ClusterId);

            silo.UseSiloMetadata(new Dictionary<string, string>
            {
                [PinToSiloStrategy.SiloMetadataKey] = "identity",
            });

            silo.UseInoJournaling();
            silo.UseInoBrainStream();
        });

        builder.Services.AddPinToSiloPlacement();

        return builder;
    }
}
