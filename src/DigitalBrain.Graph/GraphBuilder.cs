using DigitalBrain.Spi;

namespace DigitalBrain.Graph;

/// <summary>Reads the sources and builds one brain's snapshot. The only place that knows where nodes and edges come from.</summary>
internal static class GraphBuilder
{
    public static async Task<GraphSnapshot> Build(BrainId brain, IBrainObserver observer, IEnumerable<IGraphSource> sources, long revision, CancellationToken cancellationToken)
    {
        var sink = new GraphSink();
        var prefix = brain.Value + "/";
        var root = sink.Node(brain.Value, GraphNodeKind.Brain, brain.Value);

        var contracts = new Dictionary<string, string[]>(StringComparer.Ordinal);
        foreach (var type in await observer.Types(cancellationToken).ConfigureAwait(false))
        {
            if (type.Kind == TypeKind.Neuron) { contracts[type.Alias] = type.Implements; }
        }

        await foreach (var instance in observer.Instances(type: null, cancellationToken).ConfigureAwait(false))
        {
            if (!instance.Key.StartsWith(prefix, StringComparison.Ordinal) && instance.Key != brain.Value) { continue; }
            var id = instance.Key.StartsWith(prefix, StringComparison.Ordinal) && NeuronId.TryParse(instance.Key, out var parsed)
                ? parsed.ToString()
                : $"{brain.Value}/{instance.Type}/{instance.Key[prefix.Length..]}";
            if (NeuronId.TryParse(id, out var neuronId) && neuronId.Type == "brain.graph") { continue; }
            var node = sink.Node(id, KindOf(instance.Type), LabelOf(id));
            sink.Edge(root, node, GraphEdgeKind.Owns);
            if (contracts.TryGetValue(instance.Type, out var implemented))
            {
                foreach (var contract in implemented)
                {
                    sink.Edge(node, sink.Node("contract:" + contract, GraphNodeKind.Contract, contract), GraphEdgeKind.Implements);
                }
            }
        }

        foreach (var synapse in await observer.Synapses(cancellationToken).ConfigureAwait(false))
        {
            var subscriberLocal = synapse.To.StartsWith(prefix, StringComparison.Ordinal);
            var publisherLocal = synapse.From.StartsWith(prefix, StringComparison.Ordinal);
            if (!subscriberLocal && !publisherLocal) { continue; }
            var subscriber = Endpoint(sink, synapse.To, subscriberLocal, root);
            var publisher = Endpoint(sink, synapse.From, publisherLocal, root);
            sink.Edge(subscriber, publisher, GraphEdgeKind.Subscribes);
            if (publisherLocal)
            {
                sink.Edge(publisher, sink.Node("signal:" + synapse.Signal, GraphNodeKind.Signal, synapse.Signal), GraphEdgeKind.Publishes);
            }
        }

        foreach (var source in sources)
        {
            await source.Contribute(brain, sink, cancellationToken).ConfigureAwait(false);
        }

        var (groups, group) = Grouping.Assign(sink, brain);
        return Csr.Build(revision, sink, groups, group);
    }

    private static int Endpoint(GraphSink sink, string id, bool local, int root)
    {
        if (!local)
        {
            var slash = id.IndexOf('/', StringComparison.Ordinal);
            var foreign = slash > 0 ? id[..slash] : id;
            return sink.Node("external:" + foreign, GraphNodeKind.External, foreign);
        }
        if (sink.IndexOf(id) is var known and >= 0) { return known; }
        var kind = NeuronId.TryParse(id, out var neuronId) ? KindOf(neuronId.Type) : GraphNodeKind.Neuron;
        var node = sink.Node(id, kind, LabelOf(id));
        sink.Edge(root, node, GraphEdgeKind.Owns);
        return node;
    }

    internal static GraphNodeKind KindOf(string type)
        => type == "app" || type.StartsWith("app.", StringComparison.Ordinal) ? GraphNodeKind.App
         : type.StartsWith("connection", StringComparison.Ordinal) ? GraphNodeKind.Connection
         : GraphNodeKind.Neuron;

    internal static string LabelOf(string id)
        => NeuronId.TryParse(id, out var neuronId) ? $"{neuronId.Type}/{neuronId.Key}" : id;
}
