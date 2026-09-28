using DigitalBrain.Contracts;
using DigitalBrain.Core;
using Orleans;
using Orleans.Runtime;

namespace IntoChat.LocalFiles;

[Alias("intochat.workspace-assets"), Orleans.Metadata.DefaultGrainType("intochat.workspace-assets")]
internal interface IWorkspaceAssets : INeuron
{
    Task<WorkspaceAsset[]> List();
    Task<ImageAsset> Register(WorkspaceAsset asset);
    Task<ImageAsset> Read(string documentId);
}

[GenerateSerializer, Alias("intochat.workspace-asset")]
public sealed record WorkspaceAsset([property: Id(0)] ImageAsset Image, [property: Id(1)] long Bytes, [property: Id(2)] DateTimeOffset CreatedAt);

[GenerateSerializer, Alias("intochat.workspace-assets-state")]
public sealed class WorkspaceAssetsState
{
    [Id(0)] public Dictionary<string, WorkspaceAsset> Assets { get; set; } = [];
    [Id(1)] public HashSet<string> Pages { get; set; } = [];
}

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
        LocalFileStore.ValidateAssetId(this.GetPrimaryKeyString(), asset.Image.Id);
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

[Alias("intochat.workspace-asset-page"), Orleans.Metadata.DefaultGrainType("intochat.workspace-asset-page")]
internal interface IWorkspaceAssetPage : INeuron
{
    Task<WorkspaceAsset[]> List();
    Task<ImageAsset?> Read(string documentId);
    Task<bool> HasCapacity();
    Task<ImageAsset> Register(WorkspaceAsset asset);
}

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
