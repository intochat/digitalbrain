using System.Collections.Concurrent;
using System.Text.Json;
using DigitalBrain.Core.V2;

namespace DigitalBrain.Core.Runtime;

public interface IAggregateStore
{
    Task<V2AggregateSnapshot> ReadAsync(string aggregateId, CancellationToken cancellationToken = default);
    Task<V2CommitResult> CommitAsync(string aggregateId, V2CommitRequest request, CancellationToken cancellationToken = default);
    Task AppendEffectTransitionAsync(string aggregateId, EffectTransitionRecord transition, CancellationToken cancellationToken = default);
    Task<bool> TryAppendEffectTransitionAsync(
        string aggregateId,
        string effectId,
        string? expectedTransitionId,
        EffectTransitionRecord transition,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Bounds aggregate snapshots while retaining every intent which can still execute. Terminal intents are represented by
/// their final transition, and the inbox window deliberately defines the bounded command-idempotency horizon.
/// </summary>
public static class AggregateRetention
{
    public const int MaxRetainedCommits = 128;
    public const int MaxRetainedInboxRecords = 128;
    public const int MaxRetainedInactiveEffects = 128;
    public const int MaxRetainedTransitionsPerActiveEffect = 16;

    public static bool IsTerminalEffectState(string state) => state is "Succeeded" or "Failed" or "OutcomeUnknown" or "Cancelled";

    public static V2AggregateSnapshot Compact(V2AggregateSnapshot snapshot)
    {
        var commits = snapshot.Commits.TakeLast(MaxRetainedCommits).ToArray();
        var retainedCommitIds = commits.Select(static commit => commit.CommitId).ToHashSet(StringComparer.Ordinal);
        var inbox = snapshot.Inbox
            .Where(record => record.CommitId is not null && retainedCommitIds.Contains(record.CommitId))
            .TakeLast(MaxRetainedInboxRecords)
            .ToArray();

        var transitionGroups = snapshot.EffectTransitions
            .GroupBy(static transition => transition.EffectId, StringComparer.Ordinal)
            .ToArray();
        var latestByEffect = transitionGroups.ToDictionary(
            static group => group.Key,
            static group => group.Last(),
            StringComparer.Ordinal);
        var terminalEffectIds = latestByEffect
            .Where(static pair => IsTerminalEffectState(pair.Value.State))
            .Select(static pair => pair.Key)
            .ToHashSet(StringComparer.Ordinal);
        var outbox = snapshot.Outbox
            .Where(intent => !terminalEffectIds.Contains(intent.EffectId))
            .ToArray();
        var activeEffectIds = outbox.Select(static intent => intent.EffectId).ToHashSet(StringComparer.Ordinal);

        var retainedTransitionIds = new HashSet<string>(StringComparer.Ordinal);
        foreach (var group in transitionGroups.Where(group => activeEffectIds.Contains(group.Key)))
            foreach (var transition in group.TakeLast(MaxRetainedTransitionsPerActiveEffect))
                retainedTransitionIds.Add(transition.TransitionId);
        foreach (var transition in transitionGroups
                     .Where(group => !activeEffectIds.Contains(group.Key))
                     .Select(static group => group.Last())
                     .TakeLast(MaxRetainedInactiveEffects))
            retainedTransitionIds.Add(transition.TransitionId);

        var transitions = snapshot.EffectTransitions
            .Where(transition => retainedTransitionIds.Contains(transition.TransitionId))
            .ToArray();
        return snapshot with { Commits = commits, Outbox = outbox, EffectTransitions = transitions, Inbox = inbox };
    }
}

/// <summary>
/// Reference aggregate boundary. Production adapters replace the backing dictionary with one Orleans grain state/journal
/// transaction; the invariants and crash-window tests remain identical.
/// </summary>
public sealed class InMemoryAggregateStore : IAggregateStore
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
                throw new InvalidOperationException($"Aggregate commit sequence conflict; expected {entry.Sequence}, received {request.ExpectedCommitSequence}.");

            var sequence = entry.Sequence + 1;
            var commitId = "v2-commit-" + Guid.NewGuid().ToString("N");
            var events = request.Events.ToArray();
            var commit = new AggregateCommit(sequence, commitId, events, CommitSeal.Compute(events), request.CommittedAt);
            entry.Sequence = sequence;
            entry.State = request.NewState.Clone();
            entry.Commits.Add(commit);
            entry.Outbox.AddRange(request.Effects);
            entry.Inbox.Add(new V2InboxRecord(request.CommandId, commitId, request.CommittedAt));
            entry.Apply(AggregateRetention.Compact(entry.Snapshot()));
            return Task.FromResult(new V2CommitResult(true, false, commit, entry.Snapshot()));
        }
    }

    public Task AppendEffectTransitionAsync(string aggregateId, EffectTransitionRecord transition, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var entry = _entries.GetOrAdd(aggregateId, _ => new Entry());
        lock (entry.Gate)
        {
            if (!entry.EffectTransitions.Any(x => x.TransitionId == transition.TransitionId))
            {
                entry.EffectTransitions.Add(transition);
                entry.Apply(AggregateRetention.Compact(entry.Snapshot()));
            }
            return Task.CompletedTask;
        }
    }

    public Task<bool> TryAppendEffectTransitionAsync(
        string aggregateId,
        string effectId,
        string? expectedTransitionId,
        EffectTransitionRecord transition,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!string.Equals(effectId, transition.EffectId, StringComparison.Ordinal))
            throw new ArgumentException("The effect transition does not match the requested effect.", nameof(transition));
        var entry = _entries.GetOrAdd(aggregateId, _ => new Entry());
        lock (entry.Gate)
        {
            if (entry.EffectTransitions.Any(x => x.TransitionId == transition.TransitionId)) return Task.FromResult(true);
            var latest = entry.EffectTransitions.LastOrDefault(x => x.EffectId == effectId);
            if (!string.Equals(latest?.TransitionId, expectedTransitionId, StringComparison.Ordinal)) return Task.FromResult(false);
            entry.EffectTransitions.Add(transition);
            entry.Apply(AggregateRetention.Compact(entry.Snapshot()));
            return Task.FromResult(true);
        }
    }

    private sealed class Entry
    {
        public object Gate { get; } = new();
        public long Sequence;
        public JsonElement State { get; set; } = JsonElement.Parse("null");
        public List<AggregateCommit> Commits { get; } = [];
        public List<OutboxRecord> Outbox { get; } = [];
        public List<EffectTransitionRecord> EffectTransitions { get; } = [];
        public List<V2InboxRecord> Inbox { get; } = [];
        public V2AggregateSnapshot Snapshot() => new(Sequence, State.Clone(), Commits.ToArray(), Outbox.ToArray(), EffectTransitions.ToArray(), Inbox.ToArray());
        public void Apply(V2AggregateSnapshot snapshot)
        {
            Sequence = snapshot.CommitSequence;
            State = snapshot.State.Clone();
            Commits.Clear();
            Commits.AddRange(snapshot.Commits);
            Outbox.Clear();
            Outbox.AddRange(snapshot.Outbox);
            EffectTransitions.Clear();
            EffectTransitions.AddRange(snapshot.EffectTransitions);
            Inbox.Clear();
            Inbox.AddRange(snapshot.Inbox);
        }
    }
}
