using DigitalBrain.Signals;

namespace DigitalBrain.Kernel;

internal interface ISignalConsumer
{
    bool TakesUntyped { get; }
    Task Consume(JournaledSignal signal, Func<IDisposable> enter, CancellationToken ct);
}

internal class DeliveryCounters(Action<NeuronId, NeuronId?, Position, Exception>? failed = null)
{
    private long _duplicates;
    private long _reads;
    private long _failures;

    public long DuplicatesDropped => Interlocked.Read(ref _duplicates);
    public long HistoryReads => Interlocked.Read(ref _reads);
    public long ReactionFailures => Interlocked.Read(ref _failures);

    public void Duplicate() => Interlocked.Increment(ref _duplicates);
    public void Read() => Interlocked.Increment(ref _reads);

    public void Failure(NeuronId subscriber, NeuronId? source, Position position, Exception error)
    {
        Interlocked.Increment(ref _failures);
        failed?.Invoke(subscriber, source, position, error);
    }
}

internal sealed class Delivery(
    NeuronId subscriber,
    IBrainJournal journal,
    IJournalView view,
    ISignalConsumer consumer,
    Func<string, Type?> typeOf,
    TimeProvider time,
    DeliveryCounters counters,
    Action<SignalDelivery>? observe = null) : IDisposable
{
    internal const long SkipFlush = 1000;

    private readonly Lock _gate = new();
    private readonly SemaphoreSlim _nudgeGate = new(1, 1);
    private Subscriptions _held = Subscriptions.None(subscriber);
    private Subscriptions _durable = Subscriptions.None(subscriber);
    private long? _version;
    private bool _running;
    private bool _more;
    private bool _nudged;
    private int _reactions;

    public Subscriptions Subscriptions => _held;

    public async Task Initialize(CancellationToken ct = default)
    {
        var record = await journal.ReadRecord(subscriber, Subscriptions.RecordName, ct).ConfigureAwait(true);
        _version = record?.Version;
        _durable = _held = Subscriptions.Decode(subscriber, record?.Bytes ?? ReadOnlyMemory<byte>.Empty);
    }

    public async Task Nudged(Position upTo)
    {
        await _nudgeGate.WaitAsync().ConfigureAwait(false);
        try
        {
            if (_held.Entries.Count == 0 || !_held.Entries.TrueForAll(entry => entry.Cursor.Value >= upTo.Value))
            {
                await CatchUp(nudged: true).ConfigureAwait(false);
            }
            else
            {
                counters.Duplicate();
            }
        }
        finally
        {
            _nudgeGate.Release();
        }
    }

    public async Task CatchUp(bool nudged = false)
    {
        lock (_gate)
        {
            if (nudged) { _nudged = true; }
            if (_running) { _more = true; return; }
            _running = true;
        }

        try
        {
            do
            {
                bool isNudged;
                lock (_gate)
                {
                    isNudged = _nudged;
                    _nudged = false;
                }

                var initialReactions = _reactions;
                do
                {
                    lock (_gate) { _more = false; }
                    foreach (var entry in _held.Entries)
                    {
                        await Pump(entry.Source).ConfigureAwait(true);
                    }
                } while (_more);

                var noReactionRan = _reactions == initialReactions;
                await Flush(force: isNudged && (noReactionRan || _durable.Entries.Exists(e => _held.Of(e.Source)?.Cursor != e.Cursor))).ConfigureAwait(true);

                lock (_gate)
                {
                    if (!_more)
                    {
                        _running = false;
                        break;
                    }
                }
            } while (true);
        }
        finally
        {
            lock (_gate) { _running = false; }
        }
    }

    public async Task Ensure(NeuronId source, IEnumerable<string> aliases, DateTimeOffset now)
    {
        lock (_gate)
        {
            var at = view.LastOf(source);
            _held = _held.Add(source, aliases, at, now);
        }
        await Flush(force: true).ConfigureAwait(false);
    }

    public async Task EnsureBrainWide(IEnumerable<string> aliases, DateTimeOffset now, bool exact)
    {
        lock (_gate)
        {
            var at = view.Last;
            _held = _held.AddBrainWide(aliases, at, now, exact);
        }
        await Flush(force: true).ConfigureAwait(false);
    }

    public async Task Remove(NeuronId source)
    {
        lock (_gate)
        {
            _held = _held.Remove(source);
        }
        await Flush(force: true).ConfigureAwait(false);
    }

    public async Task RemoveBrainWide()
    {
        lock (_gate)
        {
            _held = _held.Remove(null);
        }
        await Flush(force: true).ConfigureAwait(false);
    }

    private async Task Pump(NeuronId? source)
    {
        while (_held.Of(source) is { } entry)
        {
            var page = await view.ReadAfter(entry.Cursor, source, (int)SkipFlush, CancellationToken.None).ConfigureAwait(true);
            counters.Read();
            if (page.Floor.Value > entry.Cursor.Value)
            {
                var from = source ?? BrainWideOf(subscriber.Brain);
                var fromSeq = source is { } sFrom ? view.SequenceAt(sFrom, entry.Cursor) : entry.Cursor.Value;
                var toSeq = source is { } sTo ? view.SequenceAt(sTo, page.Floor) : page.Floor.Value;
                var gap = new JournaledSignal(from, toSeq, new Gap(from, fromSeq, toSeq), time.GetUtcNow());
                if (!await React(entry, page.Floor, gap).ConfigureAwait(true)) { return; }
                continue;
            }

            foreach (var item in page.Signals)
            {
                if (_held.Of(source) is not { } current) { return; }
                var wanted = current.Wants(subscriber, item.Journaled.Publisher, item.Alias, typeOf)
                             && !(consumer.TakesUntyped && current.IsBrainWide && item.Journaled.Publisher == subscriber)
                             && (consumer.TakesUntyped || item.Journaled.Signal is not IUntypedSignal);
                if (!wanted)
                {
                    Move(held => held.Advance(current, item.Position, time.GetUtcNow()));
                }
                else if (!await React(current, item.Position, item.Journaled).ConfigureAwait(true))
                {
                    return;
                }
            }

            if (page.Signals.Count == (int)SkipFlush) { continue; }
            Move(held => held.Of(source) is { } done && done.Cursor.Value < page.Last.Value
                ? held.Advance(done, page.Last, time.GetUtcNow()) : held);
            return;
        }
    }

    private async Task<bool> React(Subscription entry, Position position, JournaledSignal signal)
    {
        Move(held => held.Advance(entry, position, time.GetUtcNow()));
        ReactionScope? scope = null;
        try
        {
            await consumer.Consume(signal,
                () => scope = ReactionScope.Open(subscriber, expected => new CursorMove(Subscriptions.RecordName, _held.Encode(), expected), _version),
                CancellationToken.None).ConfigureAwait(true);
        }
        catch (Exception error) when (error is not OutOfMemoryException)
        {
            counters.Failure(subscriber, entry.Source, position, error);
            if (scope is { Carried: true }) { Carried(scope.Version); }
            else if (consumer.TakesUntyped)
            {
                await Flush(force: true).ConfigureAwait(true);
                return true;
            }
            else { Move(held => held.Retreat(entry)); }
            return false;
        }

        if (scope is { Carried: true }) { Carried(scope.Version); }
        else { await Flush(force: true).ConfigureAwait(true); }
        _reactions++;
        if (signal.Signal is not Gap)
        {
            observe?.Invoke(new SignalDelivery(signal.Publisher.ToString(), subscriber.ToString(), SignalAliases.Of(signal.Signal), signal.Sequence));
        }
        return true;
    }

    private async Task Flush(bool force)
    {
        if (!force && !_held.Entries.Exists(e => e.Cursor.Value - (_durable.Of(e.Source)?.Cursor.Value ?? 0) >= SkipFlush))
        {
            return;
        }
        var scope = ReactionScope.Of(subscriber);
        var expectedVersion = scope?.Version ?? _version;
        var move = new CursorMove(Subscriptions.RecordName, _held.Encode(), expectedVersion);
        var commit = new Commit(subscriber, null, [], move, new Stamp(null, null));
        var result = await journal.Append(commit, CancellationToken.None).ConfigureAwait(true);
        Carried(result.Version);
        scope?.Carry(result.Version);
    }

    private void Move(Func<Subscriptions, Subscriptions> change) { lock (_gate) { _held = change(_held); } }
    private void Carried(long? version) { _version = version; _durable = _held; }
    private static NeuronId BrainWideOf(BrainId brain) => new(brain, "*", "*");

    public void Dispose() => _nudgeGate.Dispose();
}
