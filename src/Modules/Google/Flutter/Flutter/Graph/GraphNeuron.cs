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

    public Task Emit(GraphActivityPulse pulse)
    {
        ArgumentNullException.ThrowIfNull(pulse);
        ArgumentException.ThrowIfNullOrWhiteSpace(pulse.EventId);
        if (!Snapshot.Nodes.Any(node => node.Id == pulse.SourceId))
        { throw new ArgumentException("Activity source must be a rendered graph node.", nameof(pulse)); }
        if (pulse.TargetId is { } target && !Snapshot.Nodes.Any(node => node.Id == target))
        { throw new ArgumentException("Activity target must be a rendered graph node.", nameof(pulse)); }
        if (pulse.Kind == "SignalPublished" && pulse.TargetId is not null)
        { throw new ArgumentException("A publication cannot imply signal delivery.", nameof(pulse)); }
        return PublishAsync(new GraphActivityObserved(this.GetPrimaryKeyString(), pulse.EventId,
            pulse.SourceId, pulse.TargetId, pulse.Kind, pulse.Status, pulse.Sequence));
    }

    [ReadOnly] public Task<GraphState> Read() { Snapshot.Name = this.GetPrimaryKeyString(); return Task.FromResult(Snapshot); }
}
