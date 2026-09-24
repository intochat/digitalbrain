using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using DigitalBrain.Contracts;
using DigitalBrain.Core;
using DigitalBrain.Flutter.ImageCanvas;
using IntoChat.LocalFiles;
using Orleans;
using Orleans.Runtime;
namespace IntoChat.Apps;

[GrainType("intochat.image-document")]
internal sealed class ImageDocumentNeuron(AppSurfaceComposer surfaces,
    [PersistentState("image", DigitalBrainNames.DefaultGrainStorage)] IPersistentState<ImageDocumentState> store) : Neuron, IImageDocument
{
    public async Task<ImageDocumentState> Open(ImageAsset asset)
    {
        if (store.State.Asset is null)
        {
            var name = this.GetPrimaryKeyString();
            var surface = await surfaces.Image(name, asset, new(), 0);
            await Save(new() { Id = name.Split('/').Last(), Asset = asset, Surface = surface, Versions = [new(asset.Id, asset.Id, 0, "original")] });
        }
        return store.State;
    }
    public async Task<ImageDocumentState> Apply(ImageEditCommand command, long expectedRevision, string operationId)
    {
        ValidateOperation(operationId);
        var digest = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(command))));
        if (store.State.Receipts.TryGetValue(operationId, out var receipt))
        {
            if (receipt != digest) { throw new InvalidOperationException("The operation ID was already used for different edits."); }
            await surfaces.Image(this.GetPrimaryKeyString(), store.State.Asset!, store.State.Recipe, store.State.Revision);
            return store.State;
        }
        RequireRevision(expectedRevision);
        var asset = store.State.Asset ?? throw new KeyNotFoundException("Open an image first.");
        var recipe = ImageEdits.Apply(store.State.Recipe, command, asset.Width, asset.Height);
        await GrainFactory.GetGrain<IImageCanvas>(this.GetPrimaryKeyString() + "/canvas").RequestEdit(recipe, expectedRevision, operationId);
        await Save(store.State with { Recipe = recipe, Revision = store.State.Revision + 1, Receipts = new(store.State.Receipts) { [operationId] = digest } });
        await surfaces.Image(this.GetPrimaryKeyString(), asset, recipe, store.State.Revision);
        return store.State;
    }
    public async Task<SaveTicket> PrepareSave(long expectedRevision, string operationId)
    {
        ValidateOperation(operationId);
        if (store.State.Saves.TryGetValue(operationId, out var previous))
        {
            if (previous.Revision != expectedRevision) { throw new InvalidOperationException("Save operation already belongs to another revision."); }
            return previous;
        }
        RequireRevision(expectedRevision);
        var ticket = new SaveTicket(operationId, expectedRevision, store.State.Recipe, store.State.Asset ?? throw new KeyNotFoundException("Open an image first."));
        await Save(store.State with { Saves = new(store.State.Saves) { [operationId] = ticket } });
        return ticket;
    }
    public async Task<ImageDocumentState> CompleteSave(string operationId, SavedFile result)
    {
        var ticket = store.State.Saves.GetValueOrDefault(operationId) ?? throw new KeyNotFoundException("Save operation not found.");
        if (ticket.Result is not null && ticket.Result != result) { throw new InvalidOperationException("Save operation has a different result."); }
        await Save(store.State with { LastSavedRevision = Math.Max(store.State.LastSavedRevision, ticket.Revision), Saves = new(store.State.Saves) { [operationId] = ticket with { Result = result } } });
        return store.State;
    }
    public Task<ImageDocumentState> Read() => Task.FromResult(store.State);
    public async Task<ImageDocumentState> AddVersion(string kind, string assetId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(kind);
        ArgumentException.ThrowIfNullOrWhiteSpace(assetId);
        var version = new ImageVersion(Guid.NewGuid().ToString("n"), assetId, store.State.Versions.Count, kind);
        await Save(store.State with { Versions = [.. store.State.Versions, version] });
        return store.State;
    }
    private void RequireRevision(long revision)
    {
        if (revision != store.State.Revision) { throw new InvalidOperationException("The document changed. Reload it before retrying."); }
    }
    private static void ValidateOperation(string id)
    {
        if (!Guid.TryParse(id, out _)) { throw new ArgumentException("Use a unique operation ID."); }
    }
    private async Task Save(ImageDocumentState next)
    {
        var previous = store.State;
        store.State = next;
        try { await store.WriteStateAsync(); } catch { store.State = previous; throw; }
    }
}