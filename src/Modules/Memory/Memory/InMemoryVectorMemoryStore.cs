namespace DigitalBrain.Memory;

internal sealed class InMemoryVectorMemoryStore : IVectorMemoryStore
{
    private readonly object _gate = new();
    private readonly Dictionary<(string Name, string Namespace, string Key), VectorMemoryEntry> _entries = new();

    public Task UpsertAsync(VectorMemoryEntry entry, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(entry);
        cancellationToken.ThrowIfCancellationRequested();
        lock (_gate)
        {
            _entries[(entry.Name, entry.Namespace, entry.Key)] = entry with
            {
                Tags = entry.Tags.ToArray(),
            };
        }

        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<RecalledMemory>> SearchAsync(
        string name,
        string @namespace,
        float[] queryEmbedding,
        int limit,
        IReadOnlyDictionary<string, string>? metadataFilter,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentException.ThrowIfNullOrWhiteSpace(@namespace);
        ArgumentNullException.ThrowIfNull(queryEmbedding);
        ArgumentOutOfRangeException.ThrowIfLessThan(limit, 1);
        cancellationToken.ThrowIfCancellationRequested();

        lock (_gate)
        {
            var scored = new List<(VectorMemoryEntry Entry, float Score)>();

            foreach (var entry in _entries.Values)
            {
                if (!string.Equals(entry.Name, name, StringComparison.Ordinal))
                {
                    continue;
                }

                if (!string.Equals(entry.Namespace, @namespace, StringComparison.Ordinal))
                {
                    continue;
                }

                if (!MatchesMetadata(entry.Tags, metadataFilter))
                {
                    continue;
                }

                scored.Add((entry, CosineSimilarity(queryEmbedding, entry.Embedding)));
            }

            IReadOnlyList<RecalledMemory> matches = scored
                .OrderByDescending(static s => s.Score)
                .ThenBy(static s => s.Entry.Key, StringComparer.Ordinal)
                .Take(limit)
                .Select(static s => new RecalledMemory(
                    s.Entry.Key,
                    s.Entry.Text,
                    s.Entry.Tags.ToArray(),
                    s.Entry.Payload))
                .ToArray();

            return Task.FromResult(matches);
        }
    }

    public Task<bool> RemoveAsync(string name, string @namespace, string key, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentException.ThrowIfNullOrWhiteSpace(@namespace);
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        cancellationToken.ThrowIfCancellationRequested();

        lock (_gate)
        {
            return Task.FromResult(_entries.Remove((name, @namespace, key)));
        }
    }

    private static bool MatchesMetadata(
        IReadOnlyList<MemoryTag> tags,
        IReadOnlyDictionary<string, string>? filter)
    {
        if (filter is null || filter.Count == 0)
        {
            return true;
        }

        foreach (var (key, value) in filter)
        {
            if (!tags.Any(tag => string.Equals(tag.Name, key, StringComparison.Ordinal)
                && string.Equals(tag.Value, value, StringComparison.Ordinal)))
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
