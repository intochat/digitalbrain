using DigitalBrain.Core.Clusters;
using DigitalBrain.Core.Identity;
using DigitalBrain.Core.Runtime.Reflection;
using DigitalBrain.Core.Synapses;
using DigitalBrain.Abstractions.Ino;
using DigitalBrain.Abstractions.Distribution;

namespace DigitalBrain.Abstractions.Brain;

public interface IDigitalBrainClient
{
    string? DashboardUrl => null;

    Task<InoSessionInfo> StartInoSessionAsync(InoSessionOptions options, CancellationToken ct = default);
    IAsyncEnumerable<Synapse> WatchTimelineAsync(CancellationToken ct = default);
    IAsyncEnumerable<ChatResponse> WatchInoSessionResponsesAsync(Guid sessionId, CancellationToken ct = default);
    Task SendToInoSessionAsync(Guid sessionId, string text, CancellationToken ct = default);
    Task SendAsync(Synapse synapse, CancellationToken ct = default);
    Task<IReadOnlyList<BrainClusterInfo>> ListClustersAsync(CancellationToken ct = default);
    Task<BrainClusterInfo> GetCurrentClusterAsync(CancellationToken ct = default);
    Task<BrainClusterInfo> ConnectToClusterAsync(ClusterId clusterId, CancellationToken ct = default);
    Task SwitchCurrentClusterAsync(ClusterId clusterId, CancellationToken ct = default);
    Task<IReadOnlyList<ClusterBundleInfo>> ListClusterBundlesAsync(ClusterId clusterId, CancellationToken ct = default);
    Task<Synapse?> FindRootSynapseAsync(Guid synapseId, CancellationToken ct = default);
    Task<IReadOnlyList<BrainNeuronDescriptor>> ListActiveNeuronsAsync(CancellationToken ct = default);
    Task<BrainNeuronDescriptor?> DescribeNeuronAsync(string neuronTypeOrName, CancellationToken ct = default);
    Task<IReadOnlyList<NeuronId>> ListSubscribersAsync(string synapseTypeName, CancellationToken ct = default);
    Task<BrainSelfDescription> DescribeSelfAsync(CancellationToken ct = default);
}
