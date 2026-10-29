namespace DigitalBrain.Runtime.Orleans.Azure;

/// <summary>Hands the cloud topology to <see cref="TopologyHosting.UseTopology"/>.</summary>
public static class CloudTopologyExtensions
{
    /// <summary>The production cloud topology, passed as an additional implementation to <c>UseTopology</c>.</summary>
    public static ITopologyProfile AddCloudTopology() => new CloudProfile();
}
