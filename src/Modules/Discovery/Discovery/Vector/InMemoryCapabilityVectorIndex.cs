using DigitalBrain.Discovery.Search;

namespace DigitalBrain.Discovery.Vector;

internal sealed class InMemoryCapabilityVectorIndex(string embeddingModel) : ICapabilityVectorIndex
{
    private readonly Dictionary<CapabilityCollection, Dictionary<string, CapabilityVectorRecord>> _collections = [];

    public string EmbeddingModel { get; } = embeddingModel;

    public Task UpsertAsync(CapabilityCollection collection, IReadOnlyList<CapabilityVectorRecord> records, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var entries = _collections.TryGetValue(collection, out var existing)
            ? existing
            : _collections[collection] = new Dictionary<string, CapabilityVectorRecord>(StringComparer.Ordinal);
        foreach (var record in records)
        {
            entries[record.Id] = record;
        }

        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<CapabilityVectorMatch>> SearchAsync(
        CapabilityCollection collection,
        string workspaceId,
        float[] queryEmbedding,
        int take,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!_collections.TryGetValue(collection, out var entries))
        {
            return Task.FromResult<IReadOnlyList<CapabilityVectorMatch>>([]);
        }

        var matches = entries.Values
            .Where(record => collection == CapabilityCollection.SystemCapabilities
                || string.Equals(record.WorkspaceId, workspaceId, StringComparison.Ordinal))
            .Select(record => new CapabilityVectorMatch(record.Id, HashingCapabilityEmbedder.Cosine(record.Embedding, queryEmbedding)))
            .OrderByDescending(static match => match.Score)
            .ThenBy(static match => match.Id, StringComparer.Ordinal)
            .Take(take)
            .ToArray();
        return Task.FromResult<IReadOnlyList<CapabilityVectorMatch>>(matches);
    }
}
