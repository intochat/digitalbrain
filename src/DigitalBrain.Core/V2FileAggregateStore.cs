using System.Collections.Concurrent;
using System.Text.Json;

namespace DigitalBrain.Core.V2;

/// <summary>Local/Test durable adapter for the V2 aggregate boundary. It writes only the dedicated V2 namespace.</summary>
public sealed class FileV2AggregateStore : IV2AggregateStore
{
    private readonly string _root;
    private readonly ConcurrentDictionary<string, object> _locks = new(StringComparer.Ordinal);
    private readonly JsonSerializerOptions _options = new(JsonSerializerDefaults.Web);

    public FileV2AggregateStore(string root)
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
            if (snapshot.CommitSequence != request.ExpectedCommitSequence) throw new InvalidOperationException("V2 commit sequence conflict.");
            var events = request.Events.ToArray();
            var commit = new AggregateCommit(snapshot.CommitSequence + 1, "v2-commit-" + Guid.NewGuid().ToString("N"), events, V2CommitSeal.Compute(events), request.CommittedAt);
            var next = snapshot with
            {
                CommitSequence = commit.CommitSequence,
                State = request.NewState.Clone(),
                Commits = snapshot.Commits.Append(commit).ToArray(),
                Outbox = snapshot.Outbox.Concat(request.Effects).ToArray(),
                Inbox = snapshot.Inbox.Append(new V2InboxRecord(request.CommandId, commit.CommitId, request.CommittedAt)).ToArray()
            };
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
            WriteSnapshot(aggregateId, snapshot with { EffectTransitions = snapshot.EffectTransitions.Append(transition).ToArray() });
            return Task.CompletedTask;
        }
    }

    private object Gate(string id) => _locks.GetOrAdd(id, _ => new object());
    private string PathFor(string id) => Path.Combine(_root, Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(id))) + ".json");
    private V2AggregateSnapshot ReadSnapshot(string id)
    {
        var path = PathFor(id);
        if (!File.Exists(path)) return new V2AggregateSnapshot(0, JsonDocument.Parse("null").RootElement.Clone(), [], [], [], []);
        return JsonSerializer.Deserialize<V2AggregateSnapshot>(File.ReadAllText(path), _options) ?? throw new InvalidDataException("Invalid V2 aggregate snapshot.");
    }
    private void WriteSnapshot(string id, V2AggregateSnapshot snapshot)
    {
        var path = PathFor(id);
        var temp = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        File.WriteAllText(temp, JsonSerializer.Serialize(snapshot, _options));
        File.Move(temp, path, true);
    }
}
