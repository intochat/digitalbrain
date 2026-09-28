namespace DigitalBrain.Memory;

internal interface IVectorMemoryStore
{
    Task UpsertAsync(VectorMemoryEntry entry, CancellationToken cancellationToken);

    Task<bool> RemoveAsync(string name, string @namespace, string key, CancellationToken cancellationToken);

    Task<long> RemoveNamespaceAsync(string name, string @namespace, CancellationToken cancellationToken);
}

internal interface ILegacyVectorMemoryStore
{
    Task<LegacyMemoryPage> ReadPage(string name, string @namespace, string? cursor, int limit, CancellationToken ct);
}
internal sealed record LegacyMemoryPage(IReadOnlyList<VectorMemoryEntry> Entries, string? NextCursor);
