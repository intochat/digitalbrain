using DigitalBrain.Abstractions;
using DigitalBrain.Core;
using Microsoft.Extensions.DependencyInjection;
using Orleans.Journaling;
using Orleans.Serialization;

namespace DigitalBrain.UI;

[GrainType("diagram")]
internal sealed class DiagramNeuron : Neuron, IDiagram
{
    private const string NodeLogName = "diagram.nodes";
    private const string EdgeLogName = "diagram.edges";
    private const int RetainedNodes = 256;
    private const int RetainedEdges = 512;

    private readonly IDurableList<byte[]> _nodes;
    private readonly IDurableList<byte[]> _edges;
    private readonly Serializer<Node> _nodeSerializer;
    private readonly Serializer<Edge> _edgeSerializer;

    public DiagramNeuron()
    {
        _nodes = ServiceProvider.GetRequiredKeyedService<IDurableList<byte[]>>(NodeLogName);
        _edges = ServiceProvider.GetRequiredKeyedService<IDurableList<byte[]>>(EdgeLogName);
        _nodeSerializer = ServiceProvider.GetRequiredService<Serializer<Node>>();
        _edgeSerializer = ServiceProvider.GetRequiredService<Serializer<Edge>>();
    }

    public Task HandleAsync(Node synapse, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(synapse);
        cancellationToken.ThrowIfCancellationRequested();

        if (string.IsNullOrWhiteSpace(synapse.NodeId))
        {
            throw new NeuronAuthorizationException(
                $"Diagram '{Id}' refuses a node without an identity.");
        }

        Upsert(
            _nodes,
            _nodeSerializer.SerializeToArray(synapse),
            stored => _nodeSerializer.Deserialize(stored).NodeId == synapse.NodeId,
            RetainedNodes);

        return Task.CompletedTask;
    }

    public Task HandleAsync(Edge synapse, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(synapse);
        cancellationToken.ThrowIfCancellationRequested();

        if (string.IsNullOrWhiteSpace(synapse.EdgeId)
            || string.IsNullOrWhiteSpace(synapse.SourceNodeId)
            || string.IsNullOrWhiteSpace(synapse.TargetNodeId))
        {
            throw new NeuronAuthorizationException(
                $"Diagram '{Id}' refuses an edge without identities.");
        }

        Upsert(
            _edges,
            _edgeSerializer.SerializeToArray(synapse),
            stored => _edgeSerializer.Deserialize(stored).EdgeId == synapse.EdgeId,
            RetainedEdges);

        return Task.CompletedTask;
    }

    public Task<DiagramRead> Read()
        => Task.FromResult(new DiagramRead(
            [.. _nodes.Select(_nodeSerializer.Deserialize)],
            [.. _edges.Select(_edgeSerializer.Deserialize)]));

    private static void Upsert(
        IDurableList<byte[]> entries,
        byte[] entry,
        Func<byte[], bool> sameIdentity,
        int retained)
    {
        for (var index = entries.Count - 1; index >= 0; index--)
        {
            if (sameIdentity(entries[index]))
            {
                entries.RemoveAt(index);
            }
        }

        entries.Add(entry);
        while (entries.Count > retained)
        {
            entries.RemoveAt(0);
        }
    }
}
