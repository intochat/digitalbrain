using DigitalBrain.Contracts;
using DigitalBrain.Core;
using Orleans.Runtime;

namespace DigitalBrain.Flutter.Collection;

[GrainType(UIVocabulary.CollectionType)]
internal sealed class CollectionNeuron(
    [PersistentState("state", DigitalBrainNames.DefaultGrainStorage)] IPersistentState<CollectionState> store)
    : Neuron, ICollectionView
{
    public async Task Set(CollectionDefinition definition, long expectedRevision)
    {
        ArgumentNullException.ThrowIfNull(definition);
        if (definition.Items.Count > 500 || definition.Items.Select(x => x.Id).Distinct().Count() != definition.Items.Count) { throw new ArgumentException("Collection items must have unique IDs and contain at most 500 items."); }
        if (store.State.Revision != expectedRevision) { throw new InvalidOperationException("The UI changed. Read its latest revision before retrying."); }
        var previous = store.State;
        store.State = new() { Name = this.GetPrimaryKeyString(), Revision = checked(previous.Revision + 1), Definition = definition };
        try { await store.WriteStateAsync(); }
        catch { store.State = previous; throw; }
        await PublishAsync(new CollectionChanged(this.GetPrimaryKeyString(), store.State.Revision));
    }

    public async Task Select(string itemId, long expectedRevision)
    {
        if (!store.State.Definition.Items.Any(x => x.Id == itemId)) { throw new ArgumentException("Unknown collection item."); }
        await Set(store.State.Definition with { Selection = itemId }, expectedRevision);
    }
    public async Task Activate(string itemId, long expectedRevision)
    {
        if (store.State.Revision != expectedRevision) { throw new InvalidOperationException("The collection changed. Refresh it first."); }
        if (!store.State.Definition.Items.Any(x => x.Id == itemId && x.CanActivate)) { throw new ArgumentException("This item cannot be opened."); }
        await PublishAsync(new CollectionActivated(this.GetPrimaryKeyString(), itemId, expectedRevision));
    }
    public Task<CollectionState> Read() => Task.FromResult(store.State);
}