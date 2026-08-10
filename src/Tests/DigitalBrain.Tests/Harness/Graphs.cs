using DigitalBrain.Abstractions;
using DigitalBrain.Client;

namespace DigitalBrain.Tests.Harness;

internal static class Graphs
{
    private static readonly TimeSpan DefaultPatience = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan PollInterval = TimeSpan.FromMilliseconds(100);

    internal static Task<IReadOnlyCollection<SynapseConnection>> ConnectionsAsync(
        IDigitalBrain brain,
        NeuronId source,
        string synapseAlias)
        => brain.GetGrainProxy<ISynapseGraph>(ISynapseGraph.InstanceName).ConnectionsFrom(source, synapseAlias);

    internal static async Task<IReadOnlyCollection<SynapseConnection>> WaitForConnectionsAsync(
        IDigitalBrain brain,
        NeuronId source,
        string synapseAlias,
        int connectionCount = 1,
        TimeSpan? patience = null)
    {
        var deadline = DateTime.UtcNow + (patience ?? DefaultPatience);

        while (DateTime.UtcNow < deadline)
        {
            var connections = await ConnectionsAsync(brain, source, synapseAlias);
            if (connections.Count >= connectionCount)
            {
                return connections;
            }

            await Task.Delay(PollInterval);
        }

        throw new TimeoutException(
            $"The graph reported fewer than {connectionCount} connection(s) for ({source}, {synapseAlias}) within {patience ?? DefaultPatience}.");
    }

    internal static async Task WaitForConnectionTargetAsync(
        IDigitalBrain brain,
        NeuronId source,
        string synapseAlias,
        NeuronId target,
        TimeSpan? patience = null)
    {
        var deadline = DateTime.UtcNow + (patience ?? DefaultPatience);

        while (DateTime.UtcNow < deadline)
        {
            var connections = await ConnectionsAsync(brain, source, synapseAlias);
            if (connections.Any(connection => connection.Target == target))
            {
                return;
            }

            await Task.Delay(PollInterval);
        }

        throw new TimeoutException(
            $"The graph never routed ({source}, {synapseAlias}) at {target} within {patience ?? DefaultPatience}.");
    }

    internal static async Task WaitForNoConnectionsAsync(
        IDigitalBrain brain,
        NeuronId source,
        string synapseAlias,
        TimeSpan? patience = null)
    {
        var deadline = DateTime.UtcNow + (patience ?? DefaultPatience);

        while (DateTime.UtcNow < deadline)
        {
            var connections = await ConnectionsAsync(brain, source, synapseAlias);
            if (connections.Count == 0)
            {
                return;
            }

            await Task.Delay(PollInterval);
        }

        throw new TimeoutException(
            $"The graph kept reporting connections for ({source}, {synapseAlias}) within {patience ?? DefaultPatience}.");
    }
}
