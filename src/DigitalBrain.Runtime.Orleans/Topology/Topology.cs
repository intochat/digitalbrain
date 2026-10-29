namespace DigitalBrain.Runtime.Orleans;

/// <summary>Where a host runs. One value selects one implementation (P2-D-09).</summary>
public enum Topology
{
    /// <summary>Everything on this machine.</summary>
    Local,

    /// <summary>Local plus a connection to a cloud server.</summary>
    LocalCloud,

    /// <summary>Hosted in the cloud.</summary>
    Cloud,
}

/// <summary>Configuration spelling of <see cref="Topology"/>.</summary>
public static class TopologyExtensions
{
    /// <summary>The value written to <c>IntoChatOS:Topology</c>: <c>local</c>, <c>local+cloud</c> or <c>cloud</c>.</summary>
    public static string ToConfigValue(this Topology topology) => topology switch
    {
        Topology.Local => "local",
        Topology.LocalCloud => "local+cloud",
        Topology.Cloud => "cloud",
        _ => throw new ArgumentOutOfRangeException(nameof(topology)),
    };
}
