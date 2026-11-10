namespace DigitalBrain;

internal sealed class ProvisionalGateQueue<TState> where TState : class
{
    private const int FlushedMemory = 256;
    private readonly List<ProvisionalEntry> _provisional = [];
    private readonly HashSet<Guid> _flushed = [];
    private readonly Queue<Guid> _flushedOrder = new();

    public sealed class ProvisionalEntry(Signal signal, TState next, DateTimeOffset at, Guid ticket)
    {
        public Guid Ticket { get; } = ticket;
        public Signal Signal { get; } = signal;
        public TState Next { get; set; } = next;
        public DateTimeOffset At { get; } = at;
        public bool Committed { get; set; }
    }

    public readonly record struct FlushBatch(TState Next, Signal Cause);

    public TState EffectiveBase(TState committedState)
        => _provisional.Count > 0 ? _provisional[^1].Next : committedState;

    public bool PruneExpiredUncommitted(DateTimeOffset now, TimeSpan lease)
        => _provisional.RemoveAll(entry => !entry.Committed && now - entry.At > lease) > 0;

    public void RecordAllowance(Signal signal, TState provisionalState, DateTimeOffset now, Guid ticket)
        => _provisional.Add(new ProvisionalEntry(signal, provisionalState, now, ticket));

    public bool WasFlushed(Guid ticket) => _flushed.Contains(ticket);

    public bool TryMarkCommitted(Guid ticket)
    {
        var index = FindUncommittedIndex(ticket);
        if (index < 0)
        {
            return false;
        }

        _provisional[index].Committed = true;
        return true;
    }

    public FlushBatch? DequeueCommittedHeadRun(Signal? explicitCause = null)
    {
        var run = 0;
        while (run < _provisional.Count && _provisional[run].Committed)
        {
            run++;
        }

        if (run == 0)
        {
            return null;
        }

        var next = _provisional[run - 1].Next;
        var cause = explicitCause ?? _provisional[run - 1].Signal;
        for (var i = 0; i < run; i++)
        {
            RememberFlushed(_provisional[i].Ticket);
        }

        _provisional.RemoveRange(0, run);
        return new FlushBatch(next, cause);
    }

    public bool TryRemoveUncommitted(Guid ticket, TState committedState, out TState baseBeforeRemoved, out IReadOnlyList<ProvisionalEntry> trailingEntries)
    {
        var index = FindUncommittedIndex(ticket);
        if (index < 0)
        {
            baseBeforeRemoved = committedState;
            trailingEntries = [];
            return false;
        }

        _provisional.RemoveAt(index);
        baseBeforeRemoved = index > 0 ? _provisional[index - 1].Next : committedState;
        trailingEntries = _provisional.Skip(index).ToList();
        return true;
    }

    private int FindUncommittedIndex(Guid ticket)
        => _provisional.FindIndex(entry => !entry.Committed && entry.Ticket == ticket);

    private void RememberFlushed(Guid ticket)
    {
        if (!_flushed.Add(ticket))
        {
            return;
        }

        _flushedOrder.Enqueue(ticket);
        if (_flushedOrder.Count > FlushedMemory)
        {
            _flushed.Remove(_flushedOrder.Dequeue());
        }
    }
}
