using DigitalBrain.Memory.Qdrant;

namespace DigitalBrain.Memory;

internal sealed class QdrantVectorMemoryStore(QdrantVectorMemoryProvider provider) : IVectorMemoryStore
{
    private readonly QdrantVectorMemoryProvider _provider = provider
        ?? throw new ArgumentNullException(nameof(provider));

    public Task UpsertAsync(VectorMemoryEntry entry, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(entry);
        return _provider.UpsertAsync(
            entry.Name,
            entry.Namespace,
            entry.Key,
            entry.Text,
            entry.Tags.ToDictionary(static tag => tag.Name, static tag => tag.Value, StringComparer.Ordinal),
            entry.Payload,
            entry.Embedding,
            cancellationToken);
    }

    public Task<IReadOnlyList<RecalledMemory>> SearchAsync(
        string name,
        string @namespace,
        float[] queryEmbedding,
        int limit,
        IReadOnlyDictionary<string, string>? metadataFilter,
        CancellationToken cancellationToken)
        => _provider.SearchAsync(name, @namespace, queryEmbedding, limit, metadataFilter, cancellationToken);

    public Task<bool> RemoveAsync(string name, string @namespace, string key, CancellationToken cancellationToken)
        => _provider.RemoveAsync(name, @namespace, key, cancellationToken);
}
