namespace DigitalBrain.Memory;

internal interface IVectorMemoryStore
{
    Task UpsertAsync(VectorMemoryEntry entry, CancellationToken cancellationToken);

    Task<bool> RemoveAsync(string name, string @namespace, string key, CancellationToken cancellationToken);

    Task<long> RemoveNamespaceAsync(string name, string @namespace, CancellationToken cancellationToken);
}
