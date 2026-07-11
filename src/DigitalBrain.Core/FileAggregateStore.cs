using System.Collections.Concurrent;
using System.Text.Json;
using DigitalBrain.Core.V2;

namespace DigitalBrain.Core.Runtime;

/// <summary>Local/Test durable adapter for the aggregate boundary. It writes only the dedicated runtime namespace.</summary>
public sealed class FileAggregateStore : IAggregateStore
{
    private static readonly ConcurrentDictionary<string, object> Locks = new(StringComparer.Ordinal);
    private readonly string _root;
    private readonly JsonSerializerOptions _options = new(JsonSerializerDefaults.Web);

    public FileAggregateStore(string root)
    {
        _root = Path.Combine(root, "v2-aggregates");
        Directory.CreateDirectory(_root);
    }

    public Task<V2AggregateSnapshot> ReadAsync(string aggregateId, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (Gate(aggregateId)) return Task.FromResult(ReadSnapshot(aggregateId));
    }

    public Task<V2CommitResult> CommitAsync(string aggregateId, V2CommitRequest request, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (Gate(aggregateId))
        {
            var snapshot = ReadSnapshot(aggregateId);
            var duplicate = snapshot.Inbox.FirstOrDefault(x => x.CommandId == request.CommandId && x.CommitId is not null);
            if (duplicate?.CommitId is not null)
            {
                var existing = snapshot.Commits.Single(x => x.CommitId == duplicate.CommitId);
                return Task.FromResult(new V2CommitResult(false, true, existing, snapshot));
            }
            if (snapshot.CommitSequence != request.ExpectedCommitSequence) throw new InvalidOperationException("Aggregate commit sequence conflict.");
            var events = request.Events.ToArray();
            var commit = new AggregateCommit(snapshot.CommitSequence + 1, "v2-commit-" + Guid.NewGuid().ToString("N"), events, CommitSeal.Compute(events), request.CommittedAt);
            var next = AggregateRetention.Compact(snapshot with
            {
                CommitSequence = commit.CommitSequence,
                State = request.NewState.Clone(),
                Commits = snapshot.Commits.Append(commit).ToArray(),
                Outbox = snapshot.Outbox.Concat(request.Effects).ToArray(),
                Inbox = snapshot.Inbox.Append(new V2InboxRecord(request.CommandId, commit.CommitId, request.CommittedAt)).ToArray()
            });
            WriteSnapshot(aggregateId, next);
            return Task.FromResult(new V2CommitResult(true, false, commit, next));
        }
    }

    public Task AppendEffectTransitionAsync(string aggregateId, EffectTransitionRecord transition, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (Gate(aggregateId))
        {
            var snapshot = ReadSnapshot(aggregateId);
            if (snapshot.EffectTransitions.Any(x => x.TransitionId == transition.TransitionId)) return Task.CompletedTask;
            WriteSnapshot(aggregateId, AggregateRetention.Compact(snapshot with
            {
                EffectTransitions = snapshot.EffectTransitions.Append(transition).ToArray()
            }));
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
        lock (Gate(aggregateId))
        {
            var snapshot = ReadSnapshot(aggregateId);
            if (snapshot.EffectTransitions.Any(x => x.TransitionId == transition.TransitionId)) return Task.FromResult(true);
            var latest = snapshot.EffectTransitions.LastOrDefault(x => x.EffectId == effectId);
            if (!string.Equals(latest?.TransitionId, expectedTransitionId, StringComparison.Ordinal)) return Task.FromResult(false);
            WriteSnapshot(aggregateId, AggregateRetention.Compact(snapshot with
            {
                EffectTransitions = snapshot.EffectTransitions.Append(transition).ToArray()
            }));
            return Task.FromResult(true);
        }
    }

    private object Gate(string id) => Locks.GetOrAdd(PathFor(id), _ => new object());
    private string PathFor(string id) => Path.Combine(_root, Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(id))) + ".json");
    private V2AggregateSnapshot ReadSnapshot(string id)
    {
        var path = PathFor(id);
        if (!File.Exists(path)) return new V2AggregateSnapshot(0, JsonElement.Parse("null"), [], [], [], []);
        return JsonSerializer.Deserialize<V2AggregateSnapshot>(File.ReadAllText(path), _options) ?? throw new InvalidDataException("Invalid aggregate snapshot.");
    }
    private void WriteSnapshot(string id, V2AggregateSnapshot snapshot)
    {
        var path = PathFor(id);
        var temp = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        File.WriteAllText(temp, JsonSerializer.Serialize(snapshot, _options));
        File.Move(temp, path, true);
    }
}
