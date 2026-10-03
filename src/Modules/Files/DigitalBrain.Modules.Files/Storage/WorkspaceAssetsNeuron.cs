using DigitalBrain;
using DigitalBrain.Contracts;
using DigitalBrain.Kernel;
using Orleans;
using Orleans.Runtime;

namespace DigitalBrain.Files;

[GrainType("intochat.workspace-assets")]
internal sealed class WorkspaceAssetsNeuron([PersistentState("assets", DigitalBrainNames.DefaultGrainStorage)] IPersistentState<WorkspaceAssetsState> store) : Neuron, IWorkspaceAssets
{
    public async Task<WorkspaceAsset[]> List()
    {
        var result = new List<WorkspaceAsset>(store.State.Assets.Values);
        foreach (var page in store.State.Pages) { result.AddRange(await Page(page).List()); }
        return result.DistinctBy(asset => asset.Image.DocumentId).ToArray();
    }
    public async Task<ImageAsset> Read(string documentId)
    {
        if (store.State.Assets.TryGetValue(documentId, out var value)) { return value.Image; }
        foreach (var page in Chain(Prefix(documentId)))
        { if (await Page(page).Read(documentId) is { } image) { return image; } }
        throw new KeyNotFoundException("The workspace asset was not found.");
    }
    public async Task<ImageAsset> Register(WorkspaceAsset asset)
    {
        WorkspaceFileStore.ValidateAssetId(this.GetPrimaryKeyString(), asset.Image.Id);
        if (store.State.Assets.TryGetValue(asset.Image.DocumentId, out var previous))
        {
            if (previous.Image.Id != asset.Image.Id) { throw new InvalidOperationException("The document already identifies different content."); }
            return previous.Image;
        }
        var prefix = Prefix(asset.Image.DocumentId);
        var chain = Chain(prefix).ToArray();
        foreach (var id in chain)
        {
            if (await Page(id).Read(asset.Image.DocumentId) is { } existing)
            {
                if (existing.Id != asset.Image.Id) { throw new InvalidOperationException("The document already identifies different content."); }
                return existing;
            }
        }
        foreach (var id in chain)
        { if (await Page(id).HasCapacity()) { return await Page(id).Register(asset); } }
        prefix += "-" + chain.Length.ToString(System.Globalization.CultureInfo.InvariantCulture);
        if (!store.State.Pages.Contains(prefix))
        {
            var old = store.State;
            store.State = new() { Assets = old.Assets, Pages = new(old.Pages) { prefix } };
            try { await store.WriteStateAsync(); }
            catch { store.State = old; throw; }
        }
        return await Page(prefix).Register(asset);
    }
    private IWorkspaceAssetPage Page(string prefix) => GrainFactory.GetGrain<IWorkspaceAssetPage>(this.GetPrimaryKeyString() + "/assets/" + prefix);
    private IEnumerable<string> Chain(string prefix) => store.State.Pages.Where(page => page == prefix || page.StartsWith(prefix + "-", StringComparison.Ordinal)).Order(StringComparer.Ordinal);
    private static string Prefix(string id)
    {
        if (id.Length != 64 || id.Any(character => !char.IsAsciiHexDigit(character))) { throw new ArgumentException("Invalid workspace asset ID.", nameof(id)); }
        return id[..2].ToLowerInvariant();
    }
}
