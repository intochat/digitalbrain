using DigitalBrain.Contracts;
using DigitalBrain.Core;
using IntoChat.LocalFiles;
using Orleans;
using Orleans.Runtime;
namespace IntoChat.Apps;

[GrainType("intochat.file-explorer")]
internal sealed class FileExplorerNeuron(LocalFileStore files, AppSurfaceComposer surfaces,
    [PersistentState("files", DigitalBrainNames.DefaultGrainStorage)] IPersistentState<FileExplorerState> store) : Neuron, IFileExplorer
{
    private string Scope => this.GetPrimaryKeyString();
    public async Task<FileExplorerState> Navigate(string? folderId, int offset = 0, string sort = "name", string filter = "")
    {
        var page = await files.ListAsync(Scope, folderId, offset, sort, filter, CancellationToken.None);
        var surface = await surfaces.Files(Scope + "/apps/files", page, offset, sort, filter);
        var previous = store.State;
        store.State = new() { FolderId = page.FolderId, Revision = previous.Revision + 1, Surface = surface };
        try { await store.WriteStateAsync(); } catch { store.State = previous; throw; }
        return store.State;
    }
    public async Task<ImageDocumentState> OpenImage(string entryId)
    {
        var asset = await files.SnapshotImageAsync(Scope, entryId, CancellationToken.None);
        var document = await GrainFactory.GetGrain<IImageDocument>(Scope + "/images/" + asset.DocumentId).Open(asset);
        await surfaces.RegisterDocument(Scope, document);
        return document;
    }
    public Task<FileExplorerState> Read() => Task.FromResult(store.State);
}