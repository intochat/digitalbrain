using System.Reflection;
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
            // One source that cannot read its store this time does not blank the brain: the picture is drawn without it.
            try { await source.Contribute(brain, sink, cancellationToken).ConfigureAwait(false); }
            catch (Exception error) when (error is not OperationCanceledException) { continue; }
        }

        // calls edges come from [assembly: Calls(from, to)], written at build time by the DigitalBrain source generator:
        // every instance of the calling neuron type calls the contract node. Nothing is loaded or reflected over method bodies.
        var byType = new Dictionary<string, List<int>>(StringComparer.Ordinal);
        for (var i = 0; i < sink.Nodes.Count; i++)
        {
            if (NeuronId.TryParse(sink.Nodes[i].Id, out var id) && id.Brain == brain)
            {
                if (!byType.TryGetValue(id.Type, out var list)) { list = []; byType[id.Type] = list; }
                list.Add(i);
            }
        }
        foreach (var (from, to) in Calls())
        {
            if (!byType.TryGetValue(from, out var callers)) { continue; }
            var contract = sink.Node("contract:" + to, GraphNodeKind.Contract, to);
            foreach (var caller in callers) { sink.Edge(caller, contract, GraphEdgeKind.Calls); }
            // A plain contract resolves to the neuron type of the same name (DefaultNeuronType), so its instances implement it.
            if (byType.TryGetValue(to, out var implementors))
            {
                foreach (var implementor in implementors) { sink.Edge(implementor, contract, GraphEdgeKind.Implements); }
            }
        }

        var (groups, group) = Grouping.Assign(sink, brain);
        return Csr.Build(revision, sink, groups, group);
    }

    private static IEnumerable<(string From, string To)> Calls()
    {
        foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
        {
            if (assembly.IsDynamic) { continue; }
            CallsAttribute[] attributes;
            try { attributes = [.. assembly.GetCustomAttributes<CallsAttribute>()]; }
            catch (Exception error) when (error is TypeLoadException or System.IO.FileNotFoundException or System.Reflection.ReflectionTypeLoadException) { continue; }
            foreach (var attribute in attributes) { yield return (attribute.From, attribute.To); }
        }
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
        if (Session(sink, id) is var served and >= 0) { return served; }
        var kind = NeuronId.TryParse(id, out var neuronId) ? KindOf(neuronId.Type) : GraphNodeKind.Neuron;
        var node = sink.Node(id, kind, LabelOf(id));
        sink.Edge(root, node, GraphEdgeKind.Owns);
        return node;
    }

    /// <summary>
    /// A runtime session subscribes on behalf of the program it serves: its key is "{runtime}/{program id}". When that program is a
    /// node already, the subscription is the program's; otherwise -1 and the session is drawn as itself.
    /// </summary>
    internal static int Session(GraphSink sink, string id)
    {
        if (!NeuronId.TryParse(id, out var session) || session.Type != "runtime.session") { return -1; }
        var slash = session.Key.IndexOf('/', StringComparison.Ordinal);
        return slash > 0 && slash < session.Key.Length - 1 ? sink.IndexOf(session.Key[(slash + 1)..]) : -1;
    }

    internal static GraphNodeKind KindOf(string type)
        => type == "app" || type.StartsWith("app.", StringComparison.Ordinal) ? GraphNodeKind.App
         : type.StartsWith("connection", StringComparison.Ordinal) ? GraphNodeKind.Connection
         : GraphNodeKind.Neuron;

    internal static string LabelOf(string id)
        => NeuronId.TryParse(id, out var neuronId) ? $"{neuronId.Type}/{neuronId.Key}" : id;
}
