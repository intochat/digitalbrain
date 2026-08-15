namespace DigitalBrain.Memory;

internal sealed class InMemoryVectorMemoryStore : IVectorMemoryStore
{
    private readonly object _gate = new();
    private readonly Dictionary<(string Owner, string Namespace, string Key), VectorMemoryEntry> _entries = new();

    public Task UpsertAsync(VectorMemoryEntry entry, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(entry);
        cancellationToken.ThrowIfCancellationRequested();
        lock (_gate)
        {
            _entries[(entry.Owner, entry.Namespace, entry.Key)] = entry with
            {
                Metadata = SnapshotMetadata(entry.Metadata),
            };
        }

        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<VectorMemoryMatch>> SearchAsync(
        string owner,
        string @namespace,
        float[] queryEmbedding,
        int limit,
        IReadOnlyDictionary<string, string>? metadataFilter,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(owner);
        ArgumentException.ThrowIfNullOrWhiteSpace(@namespace);
        ArgumentNullException.ThrowIfNull(queryEmbedding);
        ArgumentOutOfRangeException.ThrowIfLessThan(limit, 1);
        cancellationToken.ThrowIfCancellationRequested();

        lock (_gate)
        {
            var scored = new List<(VectorMemoryEntry Entry, float Score)>();

            foreach (var entry in _entries.Values)
            {
                if (!string.Equals(entry.Owner, owner, StringComparison.Ordinal))
                {
                    continue;
                }

                if (!string.Equals(entry.Namespace, @namespace, StringComparison.Ordinal))
                {
                    continue;
                }

                if (!MatchesMetadata(entry.Metadata, metadataFilter))
                {
                    continue;
                }

                scored.Add((entry, CosineSimilarity(queryEmbedding, entry.Embedding)));
            }

            IReadOnlyList<VectorMemoryMatch> matches = scored
                .OrderByDescending(static s => s.Score)
                .ThenBy(static s => s.Entry.Key, StringComparer.Ordinal)
                .Take(limit)
                .Select(static s => new VectorMemoryMatch(
                    s.Entry.Key,
                    s.Entry.Text,
                    SnapshotMetadata(s.Entry.Metadata),
                    s.Entry.Payload))
                .ToArray();

            return Task.FromResult(matches);
        }
    }

    public Task<bool> RemoveAsync(string owner, string @namespace, string key, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(owner);
        ArgumentException.ThrowIfNullOrWhiteSpace(@namespace);
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        cancellationToken.ThrowIfCancellationRequested();

        lock (_gate)
        {
            return Task.FromResult(_entries.Remove((owner, @namespace, key)));
        }
    }

    public Task<IReadOnlyList<string>> ListKeysAsync(
        string owner,
        string @namespace,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(owner);
        ArgumentException.ThrowIfNullOrWhiteSpace(@namespace);
        cancellationToken.ThrowIfCancellationRequested();

        lock (_gate)
        {
            IReadOnlyList<string> keys = _entries.Values
                .Where(entry =>
                    string.Equals(entry.Owner, owner, StringComparison.Ordinal)
                    && string.Equals(entry.Namespace, @namespace, StringComparison.Ordinal))
                .Select(static entry => entry.Key)
                .Order(StringComparer.Ordinal)
                .ToArray();
            return Task.FromResult(keys);
        }
    }

    private static Dictionary<string, string> SnapshotMetadata(IReadOnlyDictionary<string, string> metadata)
        => new(metadata, StringComparer.Ordinal);

    private static bool MatchesMetadata(
        IReadOnlyDictionary<string, string> metadata,
        IReadOnlyDictionary<string, string>? filter)
    {
        if (filter is null || filter.Count == 0)
        {
            return true;
        }

        foreach (var (key, value) in filter)
        {
            if (!metadata.TryGetValue(key, out var actual)
                || !string.Equals(actual, value, StringComparison.Ordinal))
            {
                return false;
            }
        }

        return true;
    }

    private static float CosineSimilarity(float[] left, float[] right)
    {
        if (left.Length == 0 || right.Length == 0 || left.Length != right.Length)
        {
            return float.NegativeInfinity;
        }

        double dot = 0;
        double leftNorm = 0;
        double rightNorm = 0;

        for (var index = 0; index < left.Length; index++)
        {
            dot += left[index] * right[index];
            leftNorm += left[index] * left[index];
            rightNorm += right[index] * right[index];
        }

        if (leftNorm == 0 || rightNorm == 0)
        {
            return float.NegativeInfinity;
        }

        return (float)(dot / (Math.Sqrt(leftNorm) * Math.Sqrt(rightNorm)));
    }
}
