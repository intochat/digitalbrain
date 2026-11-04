using System.Collections.Concurrent;

namespace DigitalBrain.Testing;

/// <summary>An <see cref="ISourceStore"/> in memory, per brain and source key.</summary>
public sealed class InMemorySourceStore : ISourceStore
{
    private readonly ConcurrentDictionary<(BrainId Brain, string SourceKey), SourceRecord> _records = new();

    /// <inheritdoc />
    public Task<SourceRecord?> Get(BrainId brain, string sourceKey, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(sourceKey);
        return Task.FromResult(_records.TryGetValue((brain, sourceKey), out var record) ? record : null);
    }

    /// <inheritdoc />
    public Task Put(BrainId brain, SourceRecord record, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(record);
        _records[(brain, record.SourceKey)] = record;
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task Remove(BrainId brain, string sourceKey, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(sourceKey);
        _ = _records.TryRemove((brain, sourceKey), out _);
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<SourceRecord>> List(BrainId brain, CancellationToken ct) =>
        Task.FromResult<IReadOnlyList<SourceRecord>>([.. _records.Where(r => r.Key.Brain == brain).OrderBy(r => r.Key.SourceKey, StringComparer.Ordinal).Select(r => r.Value)]);
}
