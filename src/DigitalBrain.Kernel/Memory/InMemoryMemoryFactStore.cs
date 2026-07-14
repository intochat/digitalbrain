using DigitalBrain.Kernel.Contracts;

namespace DigitalBrain.Kernel.Memory;

public sealed class InMemoryMemoryFactStore : IMemoryFactStore
{
    private readonly object _gate = new();
    private readonly Dictionary<BrainOwnerId, Dictionary<string, StoredFact>> _owners = [];

    public Task<IReadOnlyList<MemoryFactSnapshot>> ListAsync(
        BrainOwnerId ownerId,
        int maximumCount,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ArgumentOutOfRangeException.ThrowIfLessThan(maximumCount, 1);
        lock (_gate)
        {
            IReadOnlyList<MemoryFactSnapshot> result = !_owners.TryGetValue(ownerId, out var facts)
                ? []
                : facts.Values
                    .OrderBy(fact => fact.Value.FactId, StringComparer.Ordinal)
                    .Take(maximumCount)
                    .Select(fact => fact.Snapshot())
                    .ToArray();
            return Task.FromResult(result);
        }
    }

    public Task<MemoryFactSnapshot?> FindAsync(
        BrainOwnerId ownerId,
        string factId,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        factId = MemoryValues.FactId(factId, nameof(factId));
        lock (_gate)
        {
            var result = _owners.TryGetValue(ownerId, out var facts) && facts.TryGetValue(factId, out var fact)
                ? fact.Snapshot()
                : null;
            return Task.FromResult(result);
        }
    }

    public Task<MemoryWriteStatus> CreateAsync(
        BrainOwnerId ownerId,
        MemoryFactSnapshot fact,
        int capacity,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        MemoryValues.FactId(fact.FactId, nameof(fact));
        lock (_gate)
        {
            if (!_owners.TryGetValue(ownerId, out var facts))
            {
                facts = new Dictionary<string, StoredFact>(StringComparer.Ordinal);
                _owners.Add(ownerId, facts);
            }
            if (facts.TryGetValue(fact.FactId, out var existing))
            {
                if (!SameContent(existing.Value, fact))
                    throw new MemoryConflictException();
                return Task.FromResult(MemoryWriteStatus.AlreadyPresent);
            }
            if (facts.Count >= capacity)
                return Task.FromResult(MemoryWriteStatus.CapacityReached);
            facts.Add(fact.FactId, new StoredFact(fact with { ETag = "1" }, 1));
            return Task.FromResult(MemoryWriteStatus.Created);
        }
    }

    public Task<MemoryFactSnapshot> ReplaceAsync(
        BrainOwnerId ownerId,
        MemoryFactSnapshot fact,
        string expectedETag,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        MemoryValues.FactId(fact.FactId, nameof(fact));
        lock (_gate)
        {
            if (!_owners.TryGetValue(ownerId, out var facts) || !facts.TryGetValue(fact.FactId, out var existing))
                throw new MemoryNotFoundException(fact.FactId);
            if (!string.Equals(existing.Version.ToString(System.Globalization.CultureInfo.InvariantCulture), expectedETag, StringComparison.Ordinal))
                throw new MemoryConflictException();
            var version = checked(existing.Version + 1);
            var updated = fact with { ETag = version.ToString(System.Globalization.CultureInfo.InvariantCulture) };
            facts[fact.FactId] = new StoredFact(updated, version);
            return Task.FromResult(updated);
        }
    }

    public Task<bool> DeleteAsync(
        BrainOwnerId ownerId,
        string factId,
        string expectedETag,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        factId = MemoryValues.FactId(factId, nameof(factId));
        lock (_gate)
        {
            if (!_owners.TryGetValue(ownerId, out var facts) || !facts.TryGetValue(factId, out var existing))
                return Task.FromResult(false);
            if (!string.Equals(existing.Version.ToString(System.Globalization.CultureInfo.InvariantCulture), expectedETag, StringComparison.Ordinal))
                throw new MemoryConflictException();
            facts.Remove(factId);
            if (facts.Count == 0)
                _owners.Remove(ownerId);
            return Task.FromResult(true);
        }
    }

    private static bool SameContent(MemoryFactSnapshot left, MemoryFactSnapshot right) =>
        string.Equals(left.Text, right.Text, StringComparison.Ordinal) &&
        left.Tags.SequenceEqual(right.Tags, StringComparer.Ordinal) &&
        left.SourceActor == right.SourceActor;

    private sealed record StoredFact(MemoryFactSnapshot Value, long Version)
    {
        internal MemoryFactSnapshot Snapshot() => Value with
        {
            Tags = Value.Tags.ToArray(),
            ETag = Version.ToString(System.Globalization.CultureInfo.InvariantCulture)
        };
    }
}
