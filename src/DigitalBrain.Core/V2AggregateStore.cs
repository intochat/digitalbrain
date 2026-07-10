using System.Collections.Concurrent;
using System.Text.Json;

namespace DigitalBrain.Core.V2;

[GenerateSerializer]
public sealed record V2InboxRecord([property: Id(0)] string CommandId, [property: Id(1)] string? CommitId, [property: Id(2)] DateTimeOffset AcceptedAt);
[GenerateSerializer]
public sealed record V2AggregateSnapshot(
    [property: Id(0)] long CommitSequence,
    [property: Id(1)] JsonElement State,
    [property: Id(2)] IReadOnlyList<AggregateCommit> Commits,
    [property: Id(3)] IReadOnlyList<OutboxRecord> Outbox,
    [property: Id(4)] IReadOnlyList<EffectTransitionRecord> EffectTransitions,
    [property: Id(5)] IReadOnlyList<V2InboxRecord> Inbox);

[GenerateSerializer]
public sealed record V2CommitRequest(
    [property: Id(0)] string CommandId,
    [property: Id(1)] long ExpectedCommitSequence,
    [property: Id(2)] JsonElement NewState,
    [property: Id(3)] IReadOnlyList<V2EventEnvelope> Events,
    [property: Id(4)] IReadOnlyList<OutboxRecord> Effects,
    [property: Id(5)] DateTimeOffset CommittedAt);

[GenerateSerializer]
public sealed record V2CommitResult([property: Id(0)] bool Accepted, [property: Id(1)] bool Duplicate, [property: Id(2)] AggregateCommit Commit, [property: Id(3)] V2AggregateSnapshot Snapshot);

public interface IV2AggregateStore
{
    Task<V2AggregateSnapshot> ReadAsync(string aggregateId, CancellationToken cancellationToken = default);
    Task<V2CommitResult> CommitAsync(string aggregateId, V2CommitRequest request, CancellationToken cancellationToken = default);
    Task AppendEffectTransitionAsync(string aggregateId, EffectTransitionRecord transition, CancellationToken cancellationToken = default);
}

/// <summary>
/// Reference V2 aggregate boundary. Production adapters replace the backing dictionary with one Orleans grain state/journal
/// transaction; the invariants and crash-window tests remain identical.
/// </summary>
public sealed class InMemoryV2AggregateStore : IV2AggregateStore
{
    private readonly ConcurrentDictionary<string, Entry> _entries = new(StringComparer.Ordinal);

    public Task<V2AggregateSnapshot> ReadAsync(string aggregateId, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var entry = _entries.GetOrAdd(aggregateId, _ => new Entry());
        lock (entry.Gate) return Task.FromResult(entry.Snapshot());
    }

    public Task<V2CommitResult> CommitAsync(string aggregateId, V2CommitRequest request, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ArgumentException.ThrowIfNullOrWhiteSpace(aggregateId);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.CommandId);
        var entry = _entries.GetOrAdd(aggregateId, _ => new Entry());
        lock (entry.Gate)
        {
            var duplicate = entry.Inbox.FirstOrDefault(x => x.CommandId == request.CommandId);
            if (duplicate is not null && duplicate.CommitId is not null)
            {
                var existing = entry.Commits.Single(x => x.CommitId == duplicate.CommitId);
                return Task.FromResult(new V2CommitResult(false, true, existing, entry.Snapshot()));
            }

            if (request.ExpectedCommitSequence != entry.Sequence)
                throw new InvalidOperationException($"V2 commit sequence conflict; expected {entry.Sequence}, received {request.ExpectedCommitSequence}.");

            var sequence = entry.Sequence + 1;
            var commitId = "v2-commit-" + Guid.NewGuid().ToString("N");
            var events = request.Events.ToArray();
            var commit = new AggregateCommit(sequence, commitId, events, V2CommitSeal.Compute(events), request.CommittedAt);
            entry.Sequence = sequence;
            entry.State = request.NewState.Clone();
            entry.Commits.Add(commit);
            entry.Outbox.AddRange(request.Effects);
            entry.Inbox.Add(new V2InboxRecord(request.CommandId, commitId, request.CommittedAt));
            return Task.FromResult(new V2CommitResult(true, false, commit, entry.Snapshot()));
        }
    }

    public Task AppendEffectTransitionAsync(string aggregateId, EffectTransitionRecord transition, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var entry = _entries.GetOrAdd(aggregateId, _ => new Entry());
        lock (entry.Gate)
        {
            if (!entry.EffectTransitions.Any(x => x.TransitionId == transition.TransitionId)) entry.EffectTransitions.Add(transition);
            return Task.CompletedTask;
        }
    }

    private sealed class Entry
    {
        public object Gate { get; } = new();
        public long Sequence;
        public JsonElement State { get; set; } = JsonDocument.Parse("null").RootElement.Clone();
        public List<AggregateCommit> Commits { get; } = [];
        public List<OutboxRecord> Outbox { get; } = [];
        public List<EffectTransitionRecord> EffectTransitions { get; } = [];
        public List<V2InboxRecord> Inbox { get; } = [];
        public V2AggregateSnapshot Snapshot() => new(Sequence, State.Clone(), Commits.ToArray(), Outbox.ToArray(), EffectTransitions.ToArray(), Inbox.ToArray());
    }
}
