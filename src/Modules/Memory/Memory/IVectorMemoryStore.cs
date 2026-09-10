namespace DigitalBrain.Memory;

internal interface IVectorMemoryStore
{
    Task UpsertAsync(VectorMemoryEntry entry, CancellationToken cancellationToken);

    Task<IReadOnlyList<RecalledMemory>> SearchAsync(
        string name,
        string @namespace,
        float[] queryEmbedding,
        int limit,
        IReadOnlyDictionary<string, string>? metadataFilter,
        CancellationToken cancellationToken);

    Task<bool> RemoveAsync(string name, string @namespace, string key, CancellationToken cancellationToken);
}
