using System.Collections.Concurrent;
using System.Text.Json;

namespace DigitalBrain.Core.Runtime;

public sealed record CommitOwnerRegistration(string OwnerId, long RegistrationSequence, long DirectoryEpoch);
public sealed record DirectoryScanCursor(long RegistrationSequence, long DirectoryEpoch);
public sealed record OwnerCommitCursor(string OwnerId, long CommitSequence);
public sealed record ProjectionCheckpoint(string Projection, string OwnerId, long CommitSequence, long DirectoryEpoch);
public sealed record PoisonRecord(string Projection, string OwnerId, long CommitSequence, string SafeReason, DateTimeOffset QuarantinedAt);

public interface ICommitSource
{
    Task<AggregateCommit?> ReadAsync(string ownerId, long afterSequence, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<CommitOwnerRegistration>> ScanOwnersAsync(DirectoryScanCursor cursor, CancellationToken cancellationToken = default);
}

public interface IProjectionSink
{
    string Name { get; }
    Task ApplyAsync(string ownerId, AggregateCommit commit, CancellationToken cancellationToken = default);
    Task<ProjectionCheckpoint?> ReadCheckpointAsync(string ownerId, CancellationToken cancellationToken = default);
    Task SaveCheckpointAsync(ProjectionCheckpoint checkpoint, CancellationToken cancellationToken = default);
    Task QuarantineAsync(PoisonRecord poison, CancellationToken cancellationToken = default);
}

/// <summary>Rebuildable projection worker. It scans the permanent owner directory repeatedly;
/// reminders are only wake-up hints and never the source of completeness.</summary>
public sealed class ProjectionWorker(ICommitSource source, IProjectionSink sink)
{
    public async Task<int> RunFullCycleAsync(DirectoryScanCursor cursor, CancellationToken cancellationToken = default)
    {
        var applied = 0;
        var owners = await source.ScanOwnersAsync(cursor, cancellationToken);
        foreach (var owner in owners.OrderBy(x => x.RegistrationSequence).ThenBy(x => x.OwnerId, StringComparer.Ordinal))
        {
            var checkpoint = await sink.ReadCheckpointAsync(owner.OwnerId, cancellationToken);
            var sequence = checkpoint?.CommitSequence ?? 0;
            while (true)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var commit = await source.ReadAsync(owner.OwnerId, sequence, cancellationToken);
                if (commit is null) break;
                try
                {
                    if (!string.Equals(commit.Checksum, CommitSeal.Compute(commit.Events), StringComparison.OrdinalIgnoreCase))
                        throw new InvalidDataException("Commit checksum mismatch.");
                    await sink.ApplyAsync(owner.OwnerId, commit, cancellationToken);
                    sequence = commit.CommitSequence;
                    await sink.SaveCheckpointAsync(new ProjectionCheckpoint(sink.Name, owner.OwnerId, sequence, owner.DirectoryEpoch), cancellationToken);
                    applied++;
                }
                catch (Exception ex) when (ex is InvalidDataException or JsonException)
                {
                    await sink.QuarantineAsync(new PoisonRecord(sink.Name, owner.OwnerId, commit.CommitSequence, Redaction.SafeSummary(ex.Message), DateTimeOffset.UtcNow), cancellationToken);
                    sequence = commit.CommitSequence;
                }
            }
        }
        return applied;
    }
}

public sealed class InMemoryCommitSource : ICommitSource
{
    private readonly ConcurrentDictionary<string, SortedDictionary<long, AggregateCommit>> _commits = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, CommitOwnerRegistration> _owners = new(StringComparer.Ordinal);
    private long _registration;
    private long _epoch = 1;

    public void RegisterOwner(string ownerId)
    {
        var sequence = Interlocked.Increment(ref _registration);
        _owners[ownerId] = new CommitOwnerRegistration(ownerId, sequence, Volatile.Read(ref _epoch));
        _commits.TryAdd(ownerId, new SortedDictionary<long, AggregateCommit>());
    }

    public void Append(string ownerId, AggregateCommit commit)
    {
        if (!_commits.TryGetValue(ownerId, out var commits)) throw new KeyNotFoundException(ownerId);
        lock (commits)
        {
            var expected = commits.Count == 0 ? 1 : commits.Keys.Max() + 1;
            if (commit.CommitSequence != expected) throw new InvalidOperationException("Commit sequence must be contiguous.");
            commits.Add(commit.CommitSequence, commit);
        }
    }

    public Task<AggregateCommit?> ReadAsync(string ownerId, long afterSequence, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!_commits.TryGetValue(ownerId, out var commits)) return Task.FromResult<AggregateCommit?>(null);
        lock (commits)
        {
            foreach (var entry in commits)
                if (entry.Key > afterSequence) return Task.FromResult<AggregateCommit?>(entry.Value);
            return Task.FromResult<AggregateCommit?>(null);
        }
    }

    public Task<IReadOnlyList<CommitOwnerRegistration>> ScanOwnersAsync(DirectoryScanCursor cursor, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult<IReadOnlyList<CommitOwnerRegistration>>(_owners.Values.Where(x => x.RegistrationSequence > cursor.RegistrationSequence || x.DirectoryEpoch > cursor.DirectoryEpoch).OrderBy(x => x.RegistrationSequence).ToArray());
    }
}

public sealed class InMemoryProjectionSink(string name) : IProjectionSink
{
    private readonly ConcurrentDictionary<string, ProjectionCheckpoint> _checkpoints = new(StringComparer.Ordinal);
    public string Name { get; } = name;
    public ConcurrentQueue<(string Owner, AggregateCommit Commit)> Applied { get; } = new();
    public ConcurrentQueue<PoisonRecord> Poison { get; } = new();
    public Task ApplyAsync(string ownerId, AggregateCommit commit, CancellationToken cancellationToken = default) { Applied.Enqueue((ownerId, commit)); return Task.CompletedTask; }
    public Task<ProjectionCheckpoint?> ReadCheckpointAsync(string ownerId, CancellationToken cancellationToken = default) { _checkpoints.TryGetValue(ownerId, out var value); return Task.FromResult(value); }
    public Task SaveCheckpointAsync(ProjectionCheckpoint checkpoint, CancellationToken cancellationToken = default) { _checkpoints[checkpoint.OwnerId] = checkpoint; return Task.CompletedTask; }
    public Task QuarantineAsync(PoisonRecord poison, CancellationToken cancellationToken = default) { Poison.Enqueue(poison); return Task.CompletedTask; }
}
