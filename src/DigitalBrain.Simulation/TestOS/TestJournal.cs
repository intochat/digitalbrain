using System.Text.Json;
using DigitalBrain.Invocation;
using DigitalBrain.Kernel;

namespace DigitalBrain.Testing;

/// <summary>
/// One brain's journal in TestOS: neuron state and the signals of each state change, held in memory.
/// Backed by <see cref="MemoryJournal"/>.
/// </summary>
public sealed class TestJournal : IBrainJournal, ISignalHistory
{
    private readonly MemoryJournal _memoryJournal;
    private readonly TimeProvider _time;
    private int _saves;

    internal TestJournal(BrainId brain, TimeProvider time)
    {
        Brain = brain;
        _time = time;
        _memoryJournal = new MemoryJournal(brain, time);
    }

    internal MemoryJournal MemoryJournal => _memoryJournal;

    /// <summary>The brain this journal belongs to.</summary>
    public BrainId Brain { get; }

    /// <summary>How many state saves were written, across all neurons.</summary>
    public int Saves => _saves;

    /// <summary>The last sequence <paramref name="publisher"/> wrote, or 0.</summary>
    public long Last(NeuronId publisher) => _memoryJournal.LastSequenceOf(publisher);

    /// <summary>The brain's last append position (the brain-wide sequence), or 0.</summary>
    internal long Position => _memoryJournal.Last.Value;

    /// <summary>The floor of <paramref name="publisher"/>: every sequence at or below it is trimmed.</summary>
    public long Floor(NeuronId publisher) => _memoryJournal.FloorOf(publisher).Value;

    /// <summary>The held signals of <paramref name="publisher"/>, in sequence order.</summary>
    public IReadOnlyList<JournaledSignal> Signals(NeuronId publisher)
    {
        var list = new List<JournaledSignal>();
        long since = 0;
        while (true)
        {
            var page = _memoryJournal.Read(publisher, since, 1000, CancellationToken.None).GetAwaiter().GetResult();
            list.AddRange(page.Signals);
            if (page.Signals.Count < 1000 || page.Signals.Count == 0 || page.Signals[^1].Sequence >= page.Last) { break; }
            since = page.Signals[^1].Sequence;
        }
        return list;
    }

    /// <inheritdoc />
    public Task<SignalPage> Read(NeuronId publisher, long since, int limit, CancellationToken ct)
        => _memoryJournal.Read(publisher, since, limit, ct);

    /// <inheritdoc />
    public Task<SignalPage> Read(long since, int limit, CancellationToken ct)
        => _memoryJournal.Read(since, limit, ct);

    /// <inheritdoc />
    public Task<SignalPage> ReadTrace(string traceId, long since, int limit, CancellationToken ct)
        => _memoryJournal.ReadTrace(traceId, since, limit, ct);

    /// <inheritdoc />
    public IAsyncEnumerable<JournaledSignal> Watch(long since, CancellationToken ct)
        => _memoryJournal.Watch(since, ct);

    Task<CommitResult> IBrainJournal.Append(Commit commit, CancellationToken ct)
    {
        if (commit.State is { Change: StateChange.Write })
        {
            Interlocked.Increment(ref _saves);
        }
        return _memoryJournal.Append(commit, ct);
    }

    Task<JournalRecord?> IBrainJournal.ReadRecord(NeuronId owner, string name, CancellationToken ct)
        => _memoryJournal.ReadRecord(owner, name, ct);

    internal Task<CommitResult> Append(Commit commit, CancellationToken ct = default)
        => ((IBrainJournal)this).Append(commit, ct);

    internal Task<JournalRecord?> ReadRecord(NeuronId owner, string name, CancellationToken ct = default)
        => _memoryJournal.ReadRecord(owner, name, ct);

    internal T? Load<T>(NeuronId id)
    {
        var record = _memoryJournal.ReadRecord(id, "state", CancellationToken.None).GetAwaiter().GetResult();
        return record is null ? default : JsonSerializer.Deserialize<T>(record.Bytes.Span, RuntimeProtocol.OptionsFor(typeof(T)));
    }

    internal IReadOnlyList<JournaledSignal> Save<T>(NeuronId id, T next, IReadOnlyList<Signal> signals)
    {
        var bytes = JsonSerializer.SerializeToUtf8Bytes(next, RuntimeProtocol.OptionsFor(next?.GetType() ?? typeof(T)));
        var stateWrite = new StateWrite("state", StateChange.Write, bytes, null);
        var stamp = TestCause.Stamp();
        var commit = new Commit(id, stateWrite, signals, null, new Stamp(stamp.TraceId, stamp.CausationId));
        var result = Append(commit, CancellationToken.None).GetAwaiter().GetResult();
        return result.Journaled(commit);
    }

    internal IReadOnlyList<JournaledSignal> Append(NeuronId publisher, IReadOnlyList<Signal> signals)
    {
        var stamp = TestCause.Stamp();
        var commit = new Commit(publisher, null, signals, null, new Stamp(stamp.TraceId, stamp.CausationId));
        var result = Append(commit, CancellationToken.None).GetAwaiter().GetResult();
        return result.Journaled(commit);
    }

    internal void Seed(NeuronId id, JsonElement state)
    {
        var bytes = JsonSerializer.SerializeToUtf8Bytes(state, RuntimeProtocol.Json);
        var commit = new Commit(id, new StateWrite("state", StateChange.Write, bytes, null), [], null, new Stamp(null, null));
        _memoryJournal.Append(commit, CancellationToken.None).GetAwaiter().GetResult();
    }

    internal void Clear(NeuronId id)
    {
        var commit = new Commit(id, new StateWrite("state", StateChange.Clear, ReadOnlyMemory<byte>.Empty, null), [], null, new Stamp(null, null));
        _memoryJournal.Append(commit, CancellationToken.None).GetAwaiter().GetResult();
    }

    internal IEnumerable<NeuronId> Publishers()
    {
        return _memoryJournal.Marks(null).Select(m => m.Publisher).Distinct();
    }

    internal void Checkpoint(NeuronId publisher, TestOSOptions options, long? lowestLive)
    {
        var cutoff = _time.GetUtcNow() - options.HistoryWindowAge;
        var signals = Signals(publisher);
        var floor = RetentionMath.Floor(Last(publisher), options.HistoryWindowSignals, signals.Count(entry => entry.At > cutoff), lowestLive);
        var floorPos = _memoryJournal.PositionAt(publisher, floor).Value;
        _memoryJournal.Trim(publisher, floor, floorPos);
    }
}
