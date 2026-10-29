using Microsoft.Extensions.Configuration;

namespace DigitalBrain.Runtime.Orleans;

/// <summary>What a topology implementation is given when it configures a host.</summary>
/// <param name="Topology">The selected topology.</param>
/// <param name="Home">Where local state lives.</param>
/// <param name="Configuration">The host configuration.</param>
/// <param name="Modules">The loaded module manifests.</param>
/// <param name="SiloPort">The silo port.</param>
/// <param name="GatewayPort">The gateway port.</param>
public sealed record TopologyContext(
    Topology Topology,
    StateHome Home,
    IConfiguration Configuration,
    IReadOnlyList<ModuleManifest> Modules,
    int SiloPort,
    int GatewayPort)
{
    /// <summary>The silo port key.</summary>
    public const string SiloPortKey = "Orleans:Endpoints:SiloPort";

    /// <summary>The gateway port key.</summary>
    public const string GatewayPortKey = "Orleans:Endpoints:GatewayPort";

    /// <summary>The default silo port.</summary>
    public const int DefaultSiloPort = 11111;

    /// <summary>The default gateway port.</summary>
    public const int DefaultGatewayPort = 30000;

    /// <summary>Builds a context, reading the ports from configuration.</summary>
    public static TopologyContext Create(Topology topology, StateHome home, IConfiguration configuration, IReadOnlyList<ModuleManifest> modules)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        return new(topology, home, configuration, modules,
            configuration.GetValue(SiloPortKey, DefaultSiloPort),
            configuration.GetValue(GatewayPortKey, DefaultGatewayPort));
    }
}
