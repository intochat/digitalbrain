using DigitalBrain.Core.Clusters;
using DigitalBrain.Core.Synapses;

namespace DigitalBrain.Abstractions.Distribution;

[GenerateSerializer]
public sealed record ClusterEndpoint(
    [property: Id(0)] ClusterId ClusterId,
    [property: Id(1)] string BaseAddress);

public sealed class ClusterEndpointOptions
{
    public const string SectionName = "DigitalBrain:Clusters";

    public IReadOnlyList<ClusterEndpoint> Endpoints { get; init; } =
    [
        new(ClusterId.Global, "memory://digitalbrain.global")
    ];
}

[GenerateSerializer]
public sealed record ClusterBundleInfo(
    [property: Id(0)] string BundleId,
    [property: Id(1)] string Version,
    [property: Id(2)] string DisplayName);

[GenerateSerializer]
public sealed record ClusterConnectionSnapshot(
    [property: Id(0)] BrainClusterInfo Cluster,
    [property: Id(1)] bool IsConnected,
    [property: Id(2)] DateTimeOffset ConnectedAtUtc);

[GenerateSerializer]
public sealed record RemoteSynapseDelivery(
    [property: Id(0)] ClusterId ClusterId,
    [property: Id(1)] Guid SynapseId,
    [property: Id(2)] string SynapseTypeName,
    [property: Id(3)] BrainScope TargetScope);

[GenerateSerializer]
public sealed record ClusterForwardResult(
    [property: Id(0)] bool Transmitted,
    [property: Id(1)] SynapseExportRejectionReason RejectionReason,
    [property: Id(2)] RemoteSynapseDelivery? Delivery,
    [property: Id(3)] Synapse? TransmittedSynapse)
{
    public static ClusterForwardResult Sent(RemoteSynapseDelivery delivery, Synapse transmittedSynapse) =>
        new(true, SynapseExportRejectionReason.None, delivery, transmittedSynapse);

    public static ClusterForwardResult Rejected(SynapseExportRejectionReason reason) =>
        new(false, reason, null, null);
}

[GenerateSerializer]
public sealed record ClusterInboundResult(
    [property: Id(0)] Synapse Synapse,
    [property: Id(1)] BrainScope AppliedScope,
    [property: Id(2)] bool WasQuarantined);

public interface IClusterConnection
{
    ClusterId ClusterId { get; }

    Task<ClusterConnectionSnapshot> ConnectAsync(CancellationToken cancellationToken = default);

    Task DisconnectAsync(CancellationToken cancellationToken = default);

    Task<IReadOnlyList<ClusterBundleInfo>> ListBundlesAsync(CancellationToken cancellationToken = default);

    Task<ClusterForwardResult> ForwardAsync(
        Synapse synapse,
        BrainScope targetScope,
        CancellationToken cancellationToken = default);
}

public interface IClusterDirectory
{
    Task<IReadOnlyList<BrainClusterInfo>> ListKnownClustersAsync(CancellationToken cancellationToken = default);

    Task<ClusterConnectionSnapshot> ConnectAsync(
        ClusterId clusterId,
        CancellationToken cancellationToken = default);

    Task DisconnectAsync(
        ClusterId clusterId,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<ClusterBundleInfo>> ListBundlesAsync(
        ClusterId clusterId,
        CancellationToken cancellationToken = default);

    Task<ClusterForwardResult> ForwardAsync(
        Synapse synapse,
        BrainScope targetScope,
        CancellationToken cancellationToken = default);

    Task<ClusterInboundResult> AcceptInboundAsync(
        Synapse synapse,
        BrainScope sourceScope,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<RemoteSynapseDelivery>> ListForwardedSynapsesAsync(
        CancellationToken cancellationToken = default);
}

[GenerateSerializer]
public sealed record ClusterConnected(
    [property: Id(0)] ClusterId ClusterId,
    [property: Id(1)] BrainScope ConnectedScope) : Synapse;

[GenerateSerializer]
public sealed record ClusterSwitched(
    [property: Id(0)] ClusterId PreviousClusterId,
    [property: Id(1)] ClusterId CurrentClusterId) : Synapse;
