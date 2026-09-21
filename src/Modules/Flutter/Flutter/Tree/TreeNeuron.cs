using System.Text.RegularExpressions;
using DigitalBrain.Contracts;
using DigitalBrain.Core;
using DigitalBrain.Flutter;
using DigitalBrain.Flutter.Tree.Signals;
using Orleans.Concurrency;
using Orleans.Runtime;

namespace DigitalBrain.Flutter.Tree;

[GrainType(UIVocabulary.TreeType)]
internal sealed class TreeNeuron([PersistentState("state", DigitalBrainNames.DefaultGrainStorage)] IPersistentState<TreeState> store)
    : Neuron<TreeState>(store), ITree
{
    public Task Set(IReadOnlyList<TreeNode> nodes)
    {
        ArgumentNullException.ThrowIfNull(nodes);
        var next = Snapshot;
        next.Name = this.GetPrimaryKeyString();
        next.Version++;
        next.Nodes = [.. nodes];
        if (!next.Nodes.Exists(node => node.Id == next.SelectedId))
        {
            next.SelectedId = next.Nodes.Count > 0 ? next.Nodes[0].Id : "";
        }

        return Save(next, new TreeChanged(this.GetPrimaryKeyString(), next.Version));
    }

    public Task Select(string id)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        var next = Snapshot;
        if (!next.Nodes.Exists(node => node.Id == id)) { throw new ArgumentException("Unknown node.", nameof(id)); }
        next.Name = this.GetPrimaryKeyString();
        next.Version++;
        next.SelectedId = id;
        return Save(next, new TreeChanged(this.GetPrimaryKeyString(), next.Version), new TreeSelected(this.GetPrimaryKeyString(), id));
    }

    [ReadOnly] public Task<TreeState> Read() { Snapshot.Name = this.GetPrimaryKeyString(); return Task.FromResult(Snapshot); }
}