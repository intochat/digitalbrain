using System.Runtime.CompilerServices;
using System.Threading.Channels;

namespace DigitalBrain.Core;

/// <summary>A process-local, metadata-only feed. Appending never awaits a reader.</summary>
public sealed class ActivityFeed(TimeProvider? clock = null)
{
    private sealed class Scope
    {
        public long Sequence;
        public long LastUsed;
        public LinkedList<ActivityEvent> Events { get; } = new();
        public HashSet<Channel<ActivityUpdate>> Subscribers { get; } = [];
    }

    private readonly object _gate = new();
    private readonly Dictionary<string, Scope> _scopes = new(StringComparer.Ordinal);
    private readonly TimeProvider _clock = clock ?? TimeProvider.System;
    private const int ScopeLimit = 2_000;
    private const int GlobalLimit = 20_000;
    private const int RegistryLimit = 256;
    private static readonly TimeSpan Retention = TimeSpan.FromMinutes(15);
    private int _count;
    private long _usage;
    private Guid _generation = Guid.NewGuid();
    public Guid Generation { get { lock (_gate) { return _generation; } } }

    public void Append(ActivityEvent item)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(item.ScopeId);
        lock (_gate)
        {
            var scope = GetScope(item.ScopeId);
            item = item with { Sequence = ++scope.Sequence };
            scope.Events.AddLast(item);
            _count++;
            Prune(scope);
            while (_count > GlobalLimit) { EvictOldest(); }
            foreach (var subscriber in scope.Subscribers.ToArray())
            {
                if (!subscriber.Writer.TryWrite(new ActivityUpdate(item, false)))
                {
                    subscriber.Writer.TryComplete();
                    scope.Subscribers.Remove(subscriber);
                }
            }
        }
    }

    public ActivitySnapshot Snapshot(string scopeId, long? afterSequence = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(scopeId);
        lock (_gate)
        {
            if (!_scopes.TryGetValue(scopeId, out var scope))
            {
                return new ActivitySnapshot([], 0, afterSequence is > 0, _clock.GetUtcNow(), Generation);
            }
            scope.LastUsed = ++_usage;
            Prune(scope);
            return SnapshotOf(scope, afterSequence);
        }
    }

    public async IAsyncEnumerable<ActivityUpdate> Watch(string scopeId, long afterSequence,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(scopeId);
        var channel = Channel.CreateBounded<ActivityUpdate>(new BoundedChannelOptions(256)
        {
            SingleReader = true, SingleWriter = false, FullMode = BoundedChannelFullMode.Wait
        });
        lock (_gate)
        {
            var scope = GetScope(scopeId);
            Prune(scope);
            var snapshot = SnapshotOf(scope, afterSequence);
            if (snapshot.Gap) { channel.Writer.TryWrite(new ActivityUpdate(null, true)); }
            foreach (var item in snapshot.Events)
            {
                if (!channel.Writer.TryWrite(new ActivityUpdate(item, false)))
                {
                    channel.Writer.TryComplete();
                    break;
                }
            }
            if (!channel.Reader.Completion.IsCompleted) { scope.Subscribers.Add(channel); }
        }
        try
        {
            await foreach (var update in channel.Reader.ReadAllAsync(cancellationToken)) { yield return update; }
            // A completed channel without cancellation means its reader fell behind.
            if (!cancellationToken.IsCancellationRequested) { yield return new ActivityUpdate(null, true); }
        }
        finally
        {
            lock (_gate)
            {
                if (_scopes.TryGetValue(scopeId, out var scope)) { scope.Subscribers.Remove(channel); }
                channel.Writer.TryComplete();
            }
        }
    }

    private Scope GetScope(string id)
    {
        if (!_scopes.TryGetValue(id, out var scope))
        {
            if (_scopes.Count >= RegistryLimit)
            {
                var victim = _scopes.MinBy(pair => pair.Value.LastUsed);
                if (victim.Value is not null)
                {
                    _count -= victim.Value.Events.Count;
                    foreach (var subscriber in victim.Value.Subscribers) { subscriber.Writer.TryComplete(); }
                    _scopes.Remove(victim.Key);
                    _generation = Guid.NewGuid();
                }
            }
            _scopes.Add(id, scope = new Scope());
        }
        scope.LastUsed = ++_usage;
        return scope;
    }

    private ActivitySnapshot SnapshotOf(Scope scope, long? after)
    {
        var oldest = scope.Events.First?.Value.Sequence ?? scope.Sequence + 1;
        var gap = (after.HasValue && (after.Value < oldest - 1 || after.Value > scope.Sequence))
            || (!after.HasValue && oldest > 1);
        return new(scope.Events.Where(e => !after.HasValue || e.Sequence > after.Value).ToArray(),
            scope.Sequence, gap, _clock.GetUtcNow(), Generation);
    }

    private void Prune(Scope scope)
    {
        var cutoff = _clock.GetUtcNow() - Retention;
        while (scope.Events.First is { } first && (scope.Events.Count > ScopeLimit || first.Value.At < cutoff))
        {
            scope.Events.RemoveFirst();
            _count--;
        }
    }

    private void EvictOldest()
    {
        Scope? oldestScope = null;
        foreach (var scope in _scopes.Values)
        {
            if (scope.Events.First is null) { continue; }
            if (oldestScope?.Events.First is null || scope.Events.First.Value.At < oldestScope.Events.First.Value.At)
            {
                oldestScope = scope;
            }
        }
        if (oldestScope?.Events.First is null) { return; }
        oldestScope.Events.RemoveFirst();
        _count--;
    }
}
