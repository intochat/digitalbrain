namespace DigitalBrain.Product.Memory.Qdrant;

internal sealed class QdrantMemoryStore(QdrantMemoryBackend backend, string workspaceToken) : IMemoryStore
{
    public Task<MemoryStoreResult> StoreAsync(MemoryEntry entry, CancellationToken cancellationToken)
        => StoreCoreAsync(entry, cancellationToken);

    public Task<IReadOnlyList<MemoryHit>> SearchAsync(MemoryQuery query, CancellationToken cancellationToken)
        => backend.SearchAsync(workspaceToken, query, cancellationToken);

    public Task RemoveAsync(string entryId, CancellationToken cancellationToken)
        => backend.RemoveAsync(workspaceToken, entryId, cancellationToken);

    private async Task<MemoryStoreResult> StoreCoreAsync(MemoryEntry entry, CancellationToken cancellationToken)
    {
        await backend.StoreAsync(workspaceToken, entry, cancellationToken).ConfigureAwait(false);
        return new MemoryStoreResult(entry.Id);
    }
}
