using System.Text.Json;
using DigitalBrain.Invocation;
using DigitalBrain.Spi;

namespace DigitalBrain.Testing;

/// <summary>
/// One brain's journal in TestOS: neuron state and the signals of each state change, held in memory. Each publisher has its own
/// sequence, starting at 1. <see cref="TestOS.ForceCheckpoint(BrainId)"/> raises a publisher's floor; entries at or below the floor are
/// trimmed, so <see cref="SignalPage.Floor"/> is the sequence a cursor jumps to when it receives <see cref="Gap"/>.
/// </summary>
/// <remarks>A per-publisher read uses publisher sequences; a brain-wide read uses the brain's own append position.</remarks>
public sealed class TestJournal : ISignalHistory
{
    private readonly Lock _gate = new();
    private readonly TimeProvider _time;
    private readonly Dictionary<NeuronId, JsonElement> _states = [];
    private readonly Dictionary<NeuronId, Log> _logs = [];
    private readonly List<(long Position, JournaledSignal Entry)> _brain = [];
    private long _position;

    internal TestJournal(BrainId brain, TimeProvider time)
    {
        Brain = brain;
        _time = time;
    }

    /// <summary>The brain this journal belongs to.</summary>
    public BrainId Brain { get; }

    /// <summary>How many state saves were written, across all neurons.</summary>
    public int Saves { get; private set; }

    /// <summary>The last sequence <paramref name="publisher"/> wrote, or 0.</summary>
    public long Last(NeuronId publisher)
    {
        lock (_gate) { return _logs.TryGetValue(publisher, out var log) ? log.Last : 0; }
    }

    /// <summary>The floor of <paramref name="publisher"/>: every sequence at or below it is trimmed.</summary>
    public long Floor(NeuronId publisher)
    {
        lock (_gate) { return _logs.TryGetValue(publisher, out var log) ? log.Floor : 0; }
    }

    /// <summary>The held signals of <paramref name="publisher"/>, in sequence order.</summary>
    public IReadOnlyList<JournaledSignal> Signals(NeuronId publisher)
    {
        lock (_gate) { return _logs.TryGetValue(publisher, out var log) ? [.. log.Entries] : []; }
    }

    /// <inheritdoc />
    /// <exception cref="UnauthorizedAccessException"><paramref name="publisher"/> belongs to another brain.</exception>
    public Task<SignalPage> Read(NeuronId publisher, long since, int limit, CancellationToken ct)
    {
        SignalPage.CheckLimit(limit);
        if (publisher.Brain != Brain) { throw new UnauthorizedAccessException($"Neuron {publisher} is not in brain {Brain}."); }
        lock (_gate)
        {
            if (!_logs.TryGetValue(publisher, out var log)) { return Task.FromResult(new SignalPage(0, 0, [])); }
            return Task.FromResult(new SignalPage(log.Floor, log.Last, [.. log.Entries.Where(entry => entry.Sequence > since).Take(limit)]));
        }
    }

    /// <inheritdoc />
    public Task<SignalPage> Read(long since, int limit, CancellationToken ct)
    {
        SignalPage.CheckLimit(limit);
        lock (_gate)
        {
            var floor = _brain.Count == 0 ? _position : _brain[0].Position - 1;
            var page = _brain.Where(item => item.Position > since).Take(limit)
                .Select(item => item.Entry with { Sequence = item.Position }).ToList();
            return Task.FromResult(new SignalPage(floor, _position, page));
        }
    }

    internal T? Load<T>(NeuronId id)
    {
        lock (_gate) { return _states.TryGetValue(id, out var json) ? json.Deserialize<T>(RuntimeProtocol.Json) : default; }
    }

    // State and signals become one entry: the state goes through the wire codec, as on the host runtime.
    internal IReadOnlyList<JournaledSignal> Save<T>(NeuronId id, T next, IReadOnlyList<Signal> signals)
    {
        var json = JsonSerializer.SerializeToElement(next, RuntimeProtocol.Json);
        lock (_gate)
        {
            _states[id] = json;
            Saves++;
            return AppendLocked(id, signals);
        }
    }

    internal IReadOnlyList<JournaledSignal> Append(NeuronId publisher, IReadOnlyList<Signal> signals)
    {
        lock (_gate) { return AppendLocked(publisher, signals); }
    }

    internal void Clear(NeuronId id)
    {
        lock (_gate) { _states.Remove(id); }
    }

    internal IEnumerable<NeuronId> Publishers()
    {
        lock (_gate) { return [.. _logs.Keys]; }
    }

    // 04-04: floor = max(0, min(lowest live cursor, last - max(window signals, signals newer than the window age))).
    internal void Checkpoint(NeuronId publisher, TestOSOptions options, long? lowestLive)
    {
        lock (_gate)
        {
            if (!_logs.TryGetValue(publisher, out var log)) { return; }
            var cutoff = _time.GetUtcNow() - options.HistoryWindowAge;
            long newer = log.Entries.Count(entry => entry.At > cutoff);
            var window = Math.Max(options.HistoryWindowSignals, newer);
            var floor = Math.Max(0, Math.Min(lowestLive ?? long.MaxValue, log.Last - window));
            if (floor <= log.Floor) { return; }
            log.Floor = floor;
            log.Entries.RemoveAll(entry => entry.Sequence <= floor);
            _brain.RemoveAll(item => item.Entry.Publisher == publisher && item.Entry.Sequence <= floor);
        }
    }

    private List<JournaledSignal> AppendLocked(NeuronId publisher, IReadOnlyList<Signal> signals)
    {
        if (publisher.Brain != Brain) { throw new UnauthorizedAccessException($"Neuron {publisher} may not write to brain {Brain}."); }
        if (!_logs.TryGetValue(publisher, out var log)) { _logs[publisher] = log = new Log(); }
        var now = _time.GetUtcNow();
        List<JournaledSignal> written = [];
        foreach (var signal in signals)
        {
            var entry = new JournaledSignal(publisher, ++log.Last, signal, now);
            log.Entries.Add(entry);
            _brain.Add((++_position, entry));
            written.Add(entry);
        }
        return written;
    }

    private sealed class Log
    {
        public List<JournaledSignal> Entries { get; } = [];

        public long Last { get; set; }

        public long Floor { get; set; }
    }
}
