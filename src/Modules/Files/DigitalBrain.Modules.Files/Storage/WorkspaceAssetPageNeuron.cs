using DigitalBrain.Contracts;
using DigitalBrain.Core;
using Orleans;
using Orleans.Runtime;

namespace DigitalBrain.Files;

[GrainType("intochat.workspace-asset-page")]
internal sealed class WorkspaceAssetPageNeuron([PersistentState("assets-page", DigitalBrainNames.DefaultGrainStorage)] IPersistentState<WorkspaceAssetsState> store) : Neuron, IWorkspaceAssetPage
{
    public Task<WorkspaceAsset[]> List() => Task.FromResult(store.State.Assets.Values.ToArray());
    public Task<ImageAsset?> Read(string documentId) => Task.FromResult(store.State.Assets.GetValueOrDefault(documentId)?.Image);
    public Task<bool> HasCapacity() => Task.FromResult(store.State.Assets.Count < 256);
    public async Task<ImageAsset> Register(WorkspaceAsset asset)
    {
        if (store.State.Assets.TryGetValue(asset.Image.DocumentId, out var previous))
        {
            if (previous.Image.Id != asset.Image.Id) { throw new InvalidOperationException("The document already identifies different content."); }
            return previous.Image;
        }
        if (store.State.Assets.Count >= 256) { throw new InvalidOperationException("This workspace asset partition is full."); }
        var old = store.State;
        store.State = new() { Assets = new(old.Assets) { [asset.Image.DocumentId] = asset } };
        try { await store.WriteStateAsync(); }
        catch { store.State = old; throw; }
        return asset.Image;
    }
}
