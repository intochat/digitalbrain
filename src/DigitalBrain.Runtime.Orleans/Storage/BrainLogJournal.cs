using DigitalBrain.Neurons;
using DigitalBrain.Kernel;

namespace DigitalBrain.Runtime.Orleans.Storage;

internal sealed class BrainLogJournal(BrainLogs logs, BrainSignalHistory history, Func<NeuronId, string> recordOwner) : IBrainJournal
{
    public BrainId Brain => history.Brain;

    public async Task<CommitResult> Append(Commit commit, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(commit);
        if (commit.Publisher.Brain != Brain) { throw new UnauthorizedAccessException($"Neuron {commit.Publisher} may not write to brain {Brain}."); }
        if (commit.IsEmpty) { throw new ArgumentException("A commit needs a state change, signals or a cursor.", nameof(commit)); }
        var log = await logs.For(Brain.Value).WaitAsync(ct).ConfigureAwait(false);
        JournaledSignalPayload[] encoded = [.. commit.Signals.Select(signal
            => log.Payloads.EncodeSignal(signal) with { TraceId = commit.Stamp.TraceId, CausationId = commit.Stamp.CausationId })];
        var entry = await log.Append(recordOwner(commit.Publisher), commit.State?.ToRecord(), commit.Cursor?.ToRecord(),
            commit.Publisher.ToString(), encoded, ct).ConfigureAwait(false);
        return CommitResult.Of(entry);
    }

    public async Task<JournalRecord?> ReadRecord(NeuronId owner, string name, CancellationToken ct = default)
        => (await logs.For(Brain.Value).WaitAsync(ct).ConfigureAwait(false)).Record(recordOwner(owner), name);

    public Task<SignalPage> Read(NeuronId publisher, long since, int limit, CancellationToken ct = default)
        => history.Read(publisher, since, limit, ct);

    public Task<SignalPage> Read(long since, int limit, CancellationToken ct = default)
        => history.Read(since, limit, ct);

    public Task<SignalPage> ReadTrace(string traceId, long since, int limit, CancellationToken ct = default)
        => history.ReadTrace(traceId, since, limit, ct);

    public IAsyncEnumerable<JournaledSignal> Watch(long since, CancellationToken ct = default)
        => history.Watch(since, ct);
}
