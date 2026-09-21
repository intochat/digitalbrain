using DigitalBrain.Contracts;
using DigitalBrain.Core;
using Orleans.Runtime;

namespace DigitalBrain.Flutter.Layout;

[GrainType(UIVocabulary.LayoutType)]
internal sealed class LayoutNeuron(
    [PersistentState("state", DigitalBrainNames.DefaultGrainStorage)] IPersistentState<LayoutState> store)
    : Neuron, ILayout
{
    public async Task Set(LayoutDefinition definition, long expectedRevision)
    {
        ArgumentNullException.ThrowIfNull(definition);
        if (definition.Mode is not ("row" or "column" or "split" or "stack")) { throw new ArgumentException("Unknown layout mode."); }
        if (!double.IsFinite(definition.Gap) || definition.Gap < 0 || definition.Gap > 128) { throw new ArgumentException("Gap must be between 0 and 128."); }
        if (definition.Extents is { } extents && (extents.Count != definition.Children.Count || extents.Any(x => !double.IsFinite(x) || x < 0 || x > 4096))) { throw new ArgumentException("Extents must match children and be between 0 and 4096."); }
        CompositionValidation.Children(definition.Children);
        if (store.State.Revision != expectedRevision) { throw new InvalidOperationException("The UI changed. Read its latest revision before retrying."); }
        var previous = store.State;
        store.State = new() { Name = this.GetPrimaryKeyString(), Revision = checked(previous.Revision + 1), Definition = definition };
        try { await store.WriteStateAsync(); }
        catch { store.State = previous; throw; }
        await PublishAsync(new LayoutChanged(this.GetPrimaryKeyString(), store.State.Revision));
    }

    public Task<LayoutState> Read() => Task.FromResult(store.State);
}
