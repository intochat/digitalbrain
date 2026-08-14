using System.Collections.ObjectModel;
using Brain.Core.Neurons;

namespace Brain.Core.Outbox;

// A deterministic test double and persistence seam. Production persistence supplies the
// same compare-and-commit boundary; this lock is deliberately not presented as distributed.
internal sealed class InMemoryOutboxStore<TState>(TState initialState) : INeuronTurnStore<TState>
{
    private readonly object _gate = new();
    private readonly List<JournalEntry> _journal = [];
    private readonly List<OutboxEntry> _emissions = [];
    private readonly List<DirectedMessage> _directedMessages = [];
    private long _version;
    private TState _state = initialState;

    public TState State
    {
        get
        {
            lock (_gate)
            {
                return _state;
            }
        }
    }

    public IReadOnlyList<JournalEntry> Journal
    {
        get
        {
            lock (_gate)
            {
                return new ReadOnlyCollection<JournalEntry>([.. _journal]);
            }
        }
    }

    public IReadOnlyList<OutboxEntry> Emissions
    {
        get
        {
            lock (_gate)
            {
                return new ReadOnlyCollection<OutboxEntry>([.. _emissions]);
            }
        }
    }

    public IReadOnlyList<DirectedMessage> DirectedMessages
    {
        get
        {
            lock (_gate)
            {
                return new ReadOnlyCollection<DirectedMessage>([.. _directedMessages]);
            }
        }
    }

    public NeuronStateSnapshot<TState> Read()
    {
        lock (_gate)
        {
            return new NeuronStateSnapshot<TState>(_version, _state);
        }
    }

    public void Commit(NeuronStateSnapshot<TState> expected, NeuronTurnCommit<TState> commit)
    {
        ArgumentNullException.ThrowIfNull(commit);
        lock (_gate)
        {
            if (_version != expected.Version)
            {
                throw new InvalidOperationException("The neuron state changed before this turn could commit.");
            }

            _journal.AddRange(commit.Journal);
            _emissions.AddRange(commit.Emissions);
            _directedMessages.AddRange(commit.DirectedMessages);
            _state = commit.State;
            _version++;
        }
    }
}
