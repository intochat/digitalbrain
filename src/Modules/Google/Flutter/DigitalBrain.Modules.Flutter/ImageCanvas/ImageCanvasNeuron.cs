using DigitalBrain.Contracts;
using DigitalBrain.Core;
using Orleans.Runtime;

namespace DigitalBrain.Flutter.ImageCanvas;

[GrainType(UIVocabulary.ImageCanvasType)]
internal sealed class ImageCanvasNeuron(
    [PersistentState("state", DigitalBrainNames.DefaultGrainStorage)] IPersistentState<ImageCanvasState> store)
    : Neuron, IImageCanvas
{
    public async Task Set(ImageCanvasDefinition definition, long expectedRevision)
    {
        ArgumentNullException.ThrowIfNull(definition);
        ArgumentException.ThrowIfNullOrWhiteSpace(definition.AssetId);
        definition.Recipe.Validate(definition.Width, definition.Height);
        if (store.State.Revision != expectedRevision) { throw new InvalidOperationException("The UI changed. Read its latest revision before retrying."); }
        var previous = store.State;
        store.State = new() { Name = this.GetPrimaryKeyString(), Revision = checked(previous.Revision + 1), Definition = definition };
        try { await store.WriteStateAsync(); }
        catch { store.State = previous; throw; }
        await PublishAsync(new ImageCanvasChanged(this.GetPrimaryKeyString(), store.State.Revision));
    }

    public async Task RequestEdit(ImageRecipe recipe, long expectedDocumentRevision, string operationId)
    {
        if (store.State.Definition.DocumentRevision != expectedDocumentRevision) { throw new InvalidOperationException("The image changed. Reload before editing."); }
        if (!Guid.TryParse(operationId, out _)) { throw new ArgumentException("An edit requires a unique operation ID."); }
        recipe.Validate(store.State.Definition.Width, store.State.Definition.Height);
        await PublishAsync(new ImageCanvasEditRequested(this.GetPrimaryKeyString(), recipe, expectedDocumentRevision, operationId));
    }
    public Task<ImageCanvasState> Read() => Task.FromResult(store.State);
}
