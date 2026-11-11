using DigitalBrain.Neurons;
using DigitalBrain.Signals;

namespace DigitalBrain.Kernel;

internal readonly record struct StampedSignal(Signal Signal, Stamp Stamp);

internal sealed class MemoryJournal(BrainId brain, TimeProvider time) : IBrainJournal, IJournalView
{
    private readonly Lock _gate = new();
    private readonly Journal<StampedSignal> _journal = new();
    private readonly SubscriptionIndex _subscriptions = new();
    private TaskCompletionSource _appended = new(TaskCreationOptions.RunContinuationsAsynchronously);

    public BrainId Brain => brain;
    public Position Last { get { lock (_gate) { return new Position(_journal.Position); } } }
    public SubscriptionIndex Subscriptions => _subscriptions;

    public Position FloorOf(NeuronId? publisher)
    {
        lock (_gate)
        {
            return publisher is { } p ? new Position(_journal.FloorOf(p.ToString())) : new Position(0);
        }
    }

    public Position LastOf(NeuronId publisher)
    {
        lock (_gate)
        {
            return new Position(_journal.LastPositionOf(publisher.ToString()));
        }
    }

    public long LastSequenceOf(NeuronId publisher)
    {
        lock (_gate)
        {
            return _journal.LastOf(publisher.ToString());
        }
    }

    public IEnumerable<AliasMark> Marks(NeuronId? publisher)
    {
        lock (_gate)
        {
            var pubString = publisher?.ToString();
            var dict = new Dictionary<(string Publisher, string Alias), Position>();
            foreach (var entry in _journal.HeldEntries)
            {
                if (pubString is null || entry.Publisher == pubString)
                {
                    for (var i = 0; i < entry.Signals.Count; i++)
                    {
                        var stamped = entry.Signals[i];
                        var alias = SignalAliases.Of(stamped.Signal);
                        dict[(entry.Publisher, alias)] = new Position(entry.FirstPosition + i);
                    }
                }
            }
            return dict.Select(kv => new AliasMark(NeuronId.Parse(kv.Key.Publisher), kv.Key.Alias, kv.Value)).ToList();
        }
    }

    public long SequenceAt(NeuronId publisher, Position position)
    {
        lock (_gate)
        {
            return _journal.SequenceAt(publisher.ToString(), position.Value);
        }
    }

    public Position PositionAt(NeuronId publisher, long sequence)
    {
        lock (_gate)
        {
            var pos = _journal.PositionAt(publisher.ToString(), sequence);
            return pos > 0 ? new Position(pos) : new Position(sequence);
        }
    }

    public Task<JournalPage> ReadAfter(Position after, NeuronId? publisher, int limit, CancellationToken ct)
    {
        lock (_gate)
        {
            var pubString = publisher?.ToString();
            var held = _journal.HeldAfter(after.Value, pubString, limit);
            var floor = FloorOf(publisher);
            var positioned = new List<PositionedSignal>();
            foreach (var entry in held)
            {
                for (var i = 0; i < entry.Signals.Count; i++)
                {
                    var pos = new Position(entry.FirstPosition + i);
                    if (pos.Value <= after.Value) { continue; }
                    var stamped = entry.Signals[i];
                    var pubId = NeuronId.Parse(entry.Publisher);
                    var seq = publisher is null ? (entry.FirstPosition + i) : (entry.FirstSignal + i);
                    var jSignal = new JournaledSignal(pubId, seq, stamped.Signal,
                        DateTimeOffset.FromUnixTimeMilliseconds(entry.AtUnixMs), stamped.Stamp.TraceId, stamped.Stamp.CausationId);
                    positioned.Add(new PositionedSignal(pos, SignalAliases.Of(stamped.Signal), jSignal));
                }
            }
            var pageLast = publisher is not null ? LastOf(publisher.Value) : Last;
            return Task.FromResult(new JournalPage(floor, pageLast, positioned.Take(limit).ToList()));
        }
    }

    public Task<CommitResult> Append(Commit commit, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(commit);
        if (commit.Publisher.Brain != Brain) { throw new UnauthorizedAccessException($"Neuron {commit.Publisher} may not write to brain {Brain}."); }
        if (commit.IsEmpty) { throw new ArgumentException("A commit needs a state change, signals or a cursor.", nameof(commit)); }

        var publisher = commit.Publisher.ToString();
        StampedSignal[] signals = [.. commit.Signals.Select(signal => new StampedSignal(signal, commit.Stamp))];
        TaskCompletionSource prior;
        JournalEntry<StampedSignal> entry;
        lock (_gate)
        {
            entry = _journal.Decide(publisher, commit.State?.ToRecord(), commit.Cursor?.ToRecord(), publisher, signals, time.GetUtcNow().ToUnixTimeMilliseconds());
            _journal.Apply(entry);

            if (commit.Cursor is { } cursor)
            {
                _subscriptions.Fold(commit.Publisher, cursor.Json, StateChange.Write, owner: publisher);
            }
            if (commit.State is { Name: DigitalBrain.Kernel.Subscriptions.RecordName } subWrite)
            {
                _subscriptions.Fold(commit.Publisher, subWrite.Json, subWrite.Change, owner: publisher);
            }

            prior = _appended;
            _appended = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        }
        prior.TrySetResult();
        return Task.FromResult(CommitResult.Of(entry));
    }

    public Task<JournalRecord?> ReadRecord(NeuronId owner, string name, CancellationToken ct = default)
    {
        lock (_gate)
        {
            return Task.FromResult(_journal.Record(owner.ToString(), name));
        }
    }

    public void Trim(NeuronId publisher, long floorSequence, long floorPosition)
    {
        lock (_gate)
        {
            _journal.Trim(publisher.ToString(), floorSequence, floorPosition);
        }
    }

    public void RestoreSnapshot(MemoryJournal source)
    {
        ArgumentNullException.ThrowIfNull(source);
        lock (_gate)
        {
            lock (source._gate)
            {
                _journal.Restore(source._journal.Sequence, source._journal.Position, source._journal.SnapshotRecords(), source._journal.SnapshotMarks(), source._journal.HeldEntries);
                foreach (var sub in source._subscriptions.All)
                {
                    _subscriptions.Fold(sub, source._subscriptions.OwnerOf(sub.Subscriber));
                }
            }
        }
    }

    public Task<SignalPage> Read(NeuronId publisher, long since, int limit, CancellationToken ct)
    {
        SignalPage.CheckLimit(limit);
        if (publisher.Brain != Brain) { throw new UnauthorizedAccessException($"Neuron {publisher} is not in brain {Brain}."); }
        lock (_gate)
        {
            var pubString = publisher.ToString();
            var floor = _journal.FloorOf(pubString);
            var last = _journal.LastOf(pubString);
            var held = new List<JournaledSignal>();
            foreach (var entry in _journal.HeldEntries)
            {
                if (entry.Publisher == pubString)
                {
                    for (var i = 0; i < entry.Signals.Count; i++)
                    {
                        var seq = entry.FirstSignal + i;
                        if (seq > since)
                        {
                            var stamped = entry.Signals[i];
                            held.Add(new JournaledSignal(publisher, seq, stamped.Signal,
                                DateTimeOffset.FromUnixTimeMilliseconds(entry.AtUnixMs), stamped.Stamp.TraceId, stamped.Stamp.CausationId));
                        }
                    }
                }
            }
            return Task.FromResult(new SignalPage(floor, last, held.Take(limit).ToList()));
        }
    }

    public Task<SignalPage> Read(long since, int limit, CancellationToken ct)
    {
        SignalPage.CheckLimit(limit);
        lock (_gate)
        {
            var floor = _journal.HeldEntries.Count == 0 ? _journal.Position : _journal.HeldEntries[0].FirstPosition - 1;
            var held = new List<JournaledSignal>();
            foreach (var entry in _journal.HeldEntries)
            {
                for (var i = 0; i < entry.Signals.Count; i++)
                {
                    var pos = entry.FirstPosition + i;
                    if (pos > since)
                    {
                        var stamped = entry.Signals[i];
                        var pub = NeuronId.Parse(entry.Publisher);
                        held.Add(new JournaledSignal(pub, pos, stamped.Signal,
                            DateTimeOffset.FromUnixTimeMilliseconds(entry.AtUnixMs), stamped.Stamp.TraceId, stamped.Stamp.CausationId));
                    }
                }
            }
            return Task.FromResult(new SignalPage(floor, _journal.Position, held.Take(limit).ToList()));
        }
    }

    public Task<SignalPage> ReadTrace(string traceId, long since, int limit, CancellationToken ct)
    {
        ArgumentException.ThrowIfNullOrEmpty(traceId);
        SignalPage.CheckLimit(limit);
        lock (_gate)
        {
            var floor = _journal.HeldEntries.Count == 0 ? _journal.Position : _journal.HeldEntries[0].FirstPosition - 1;
            var held = new List<JournaledSignal>();
            foreach (var entry in _journal.HeldEntries)
            {
                for (var i = 0; i < entry.Signals.Count; i++)
                {
                    var pos = entry.FirstPosition + i;
                    var stamped = entry.Signals[i];
                    if (pos > since && stamped.Stamp.TraceId == traceId)
                    {
                        var pub = NeuronId.Parse(entry.Publisher);
                        held.Add(new JournaledSignal(pub, pos, stamped.Signal,
                            DateTimeOffset.FromUnixTimeMilliseconds(entry.AtUnixMs), stamped.Stamp.TraceId, stamped.Stamp.CausationId));
                    }
                }
            }
            return Task.FromResult(new SignalPage(floor, _journal.Position, held.Take(limit).ToList()));
        }
    }

    public IAsyncEnumerable<JournaledSignal> Watch(long since, CancellationToken ct)
        => SignalTail.Poll(Read, since, TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(1), ct, () => { lock (_gate) { return _appended.Task; } });

    internal Journal<StampedSignal> InternalJournal => _journal;
}
