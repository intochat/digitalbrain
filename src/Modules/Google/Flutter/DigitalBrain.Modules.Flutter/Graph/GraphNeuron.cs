using System.Text.RegularExpressions;
using DigitalBrain.Contracts;
using DigitalBrain.Core;
using DigitalBrain.Flutter;
using DigitalBrain.Flutter.Graph.Signals;
using Orleans.Concurrency;
using Orleans.Runtime;

namespace DigitalBrain.Flutter.Graph;

[GrainType(UIVocabulary.GraphType)]
internal sealed class GraphNeuron([PersistentState("state", DigitalBrainNames.DefaultGrainStorage)] IPersistentState<GraphState> store)
    : Neuron<GraphState>(store), IGraph
{
    public Task Render(string title, IReadOnlyList<GraphNode> nodes, IReadOnlyList<GraphEdge> edges)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(title);
        ArgumentNullException.ThrowIfNull(nodes);
        ArgumentNullException.ThrowIfNull(edges);
        var next = Snapshot;
        next.Name = this.GetPrimaryKeyString();
        next.Version++;
        next.Title = title.Trim();
        next.Nodes = [.. nodes];
        next.Edges = [.. edges];
        return Save(next, new GraphChanged(this.GetPrimaryKeyString(), next.Version));
    }

    [ReadOnly] public Task<GraphState> Read() { Snapshot.Name = this.GetPrimaryKeyString(); return Task.FromResult(Snapshot); }
}