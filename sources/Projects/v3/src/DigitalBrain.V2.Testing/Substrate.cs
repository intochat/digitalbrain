using DigitalBrain.V2.Core.Runtime;
using Microsoft.Extensions.Hosting;
using Orleans.Configuration;
using Orleans.Hosting;
using Orleans.Serialization.Configuration;
using System.Net;
using System.Net.Sockets;
using System.Reflection;

namespace DigitalBrain.V2.Testing;

// The smallest real substrate: a localhost silo with an in-memory timeline. No persistence,
// no clustering infrastructure, no Aspire. Enough for a Simulation to fire a synapse and
// observe the broadcast.
public static class Substrate
{
    public static Task<IHost> StartAsync(CancellationToken ct = default) =>
        StartAsync([], ct);

    public static async Task<IHost> StartAsync(IEnumerable<Assembly> applicationParts, CancellationToken ct = default)
    {
        var parts = applicationParts.Distinct().ToArray();
        var ports = AllocatePorts();
        var cluster = "DigitalBrainV2-" + Guid.NewGuid().ToString("N");
        var host = Host.CreateDefaultBuilder()
            .UseOrleans(silo =>
            {
                silo.UseLocalhostClustering(ports.SiloPort, ports.GatewayPort, serviceId: cluster, clusterId: cluster)
                    .AddMemoryGrainStorage("PubSubStore")
                    .AddMemoryStreams(SynapseStream.ProviderName);

                if (parts.Length > 0)
                {
                    var generatedTypes = parts.SelectMany(LoadableTypes).ToArray();

                    silo.Configure<TypeManifestOptions>(options =>
                    {
                        foreach (var type in generatedTypes.Where(type => type.FullName is not null))
                        {
                            options.AllowedTypes.Add(type.FullName!);
                        }
                    });

                    silo.Configure<GrainTypeOptions>(options =>
                    {
                        foreach (var type in generatedTypes)
                        {
                            if (type is { IsClass: true, IsAbstract: false } && typeof(Neuron).IsAssignableFrom(type))
                            {
                                options.Classes.Add(type);
                            }
                            else if (type.IsInterface && typeof(INeuron).IsAssignableFrom(type))
                            {
                                options.Interfaces.Add(type);
                            }
                        }
                    });
                }
            })
            .Build();

        await host.StartAsync(ct);
        return host;
    }

    private static Type[] LoadableTypes(Assembly assembly)
    {
        try
        {
            return assembly.GetTypes();
        }
        catch (ReflectionTypeLoadException ex)
        {
            return ex.Types.Where(type => type is not null).Cast<Type>().ToArray();
        }
    }

    private static PortPair AllocatePorts()
    {
        var siloPort = AllocatePort();
        var gatewayPort = AllocatePort();
        while (gatewayPort == siloPort)
        {
            gatewayPort = AllocatePort();
        }

        return new PortPair(siloPort, gatewayPort);
    }

    private static int AllocatePort()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        try
        {
            return ((IPEndPoint)listener.LocalEndpoint).Port;
        }
        finally
        {
            listener.Stop();
        }
    }

    private readonly record struct PortPair(int SiloPort, int GatewayPort);
}
