using DigitalBrain.Abstractions;
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
            entry.Owner,
            entry.Namespace,
            entry.Key,
            entry.Text,
            entry.Metadata,
            entry.Payload,
            entry.Embedding,
            cancellationToken);
    }

    public async Task<IReadOnlyList<VectorMemoryMatch>> SearchAsync(
        string owner,
        string @namespace,
        float[] queryEmbedding,
        int limit,
        IReadOnlyDictionary<string, string>? metadataFilter,
        CancellationToken cancellationToken)
    {
        var hits = await _provider.SearchAsync(
            owner,
            @namespace,
            queryEmbedding,
            limit,
            metadataFilter,
            cancellationToken);

        return hits
            .Select(static hit => new VectorMemoryMatch(hit.Key, hit.Text, hit.Metadata, hit.Payload))
            .ToArray();
    }

    public Task<bool> RemoveAsync(string owner, string @namespace, string key, CancellationToken cancellationToken)
        => _provider.RemoveAsync(owner, @namespace, key, cancellationToken);

    public Task<IReadOnlyList<string>> ListKeysAsync(
        string owner,
        string @namespace,
        CancellationToken cancellationToken)
        => _provider.ListKeysAsync(owner, @namespace, cancellationToken);
}
