using System.Text.RegularExpressions;
using DigitalBrain.Contracts;
using DigitalBrain.Core;
using DigitalBrain.Flutter;
using DigitalBrain.Flutter.Expander.Signals;
using Orleans.Concurrency;
using Orleans.Runtime;

namespace DigitalBrain.Flutter.Expander;

[GrainType(UIVocabulary.ExpanderType)]
internal sealed class ExpanderNeuron([PersistentState("state", DigitalBrainNames.DefaultGrainStorage)] IPersistentState<ExpanderState> store)
    : Neuron<ExpanderState>(store), IExpander
{
    public Task Set(string header, bool expanded, IReadOnlyList<UiChildRef>? children = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(header);
        var next = Snapshot;
        next.Name = this.GetPrimaryKeyString();
        next.Version++;
        next.Header = header.Trim();
        next.Expanded = expanded;
        next.Children = [.. children ?? []];
        return Save(next, new ExpanderChanged(this.GetPrimaryKeyString(), next.Expanded));
    }

    public Task Toggle()
    {
        var next = Snapshot;
        next.Name = this.GetPrimaryKeyString();
        next.Version++;
        next.Expanded = !next.Expanded;
        return Save(next, new ExpanderChanged(this.GetPrimaryKeyString(), next.Expanded));
    }

    [ReadOnly] public Task<ExpanderState> Read() { Snapshot.Name = this.GetPrimaryKeyString(); return Task.FromResult(Snapshot); }
}