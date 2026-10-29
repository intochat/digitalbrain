using DigitalBrain.Spi;

namespace DigitalBrain.Tests.Unit.Neurons;

// Records what a neuron asks of its runtime: state writes, publishes and Get calls.
internal sealed class RecordingContext(NeuronId id) : INeuronContext, INeuronState, ISynapses, ISignalHistory
{
    private object? _stored;

    public List<(object State, IReadOnlyList<Signal> Signals)> Writes { get; } = [];

    public List<Signal> Published { get; } = [];

    public List<NeuronId> Gets { get; } = [];

    public Dictionary<NeuronId, object> Neurons { get; } = [];

    public List<Func<Task>> Deferred { get; } = [];

    public NeuronId Id { get; } = id;

    public IServiceProvider Services => throw new NotSupportedException();

    public TimeProvider Time => TimeProvider.System;

    public INeuronState State => this;

    public ITimers Timers => throw new NotSupportedException();

    public IReminders Reminders => throw new NotSupportedException();

    public IOperations Operations => throw new NotSupportedException();
    public DigitalBrain.Confirmation.IConfirmations Confirmations => throw new NotSupportedException();

    public ISynapses Synapses => this;

    public ISignalHistory History => this;

    public List<Synapse> SynapseList { get; } = [];

    public List<NeuronId> Removed { get; } = [];

    public List<JournaledSignal> Journal { get; } = [];

    public long Floor { get; set; } = 1;

    public void Store(object state) => _stored = state;

    public Task Publish(Signal signal)
    {
        Published.Add(signal);
        return Task.CompletedTask;
    }

    public T Get<T>(NeuronId id) where T : class
    {
        Gets.Add(id);
        return (T)Neurons[id];
    }

    public void Defer(Func<Task> work) => Deferred.Add(work);

    /// <summary>When set, <see cref="Load{T}"/> completes later on a thread-pool thread, as a real store's read can.</summary>
    public bool LoadsOffThread { get; set; }

    public Task<T?> Load<T>()
    {
        var value = _stored is T stored ? stored : default;
        return LoadsOffThread ? Task.Run(async () => { await Task.Delay(20); return value; }) : Task.FromResult(value);
    }

    public Task Save<T>(T next, IReadOnlyList<Signal> signals)
    {
        Writes.Add((next!, [.. signals]));
        _stored = next;
        return Task.CompletedTask;
    }

    public Task Clear()
    {
        _stored = null;
        return Task.CompletedTask;
    }

    public Task Ensure<TSignal>(NeuronId from) where TSignal : Signal => Ensure(from, typeof(TSignal));

    public Task Ensure(NeuronId from, params Type[] signals)
    {
        if (from == Id) { throw new ArgumentException("A neuron does not subscribe to itself.", nameof(from)); }
        foreach (var signal in signals)
        {
            var synapse = new Synapse(from, signal, Id);
            if (!SynapseList.Contains(synapse)) { SynapseList.Add(synapse); }
        }
        return Task.CompletedTask;
    }

    public Task Remove(NeuronId from)
    {
        Removed.Add(from);
        SynapseList.RemoveAll(synapse => synapse.From == from);
        return Task.CompletedTask;
    }

    public Task<SignalPage> Read(NeuronId publisher, long since, int limit, CancellationToken ct)
        => Task.FromResult(Page(entry => entry.Publisher == publisher, since, limit));

    public Task<SignalPage> Read(long since, int limit, CancellationToken ct)
        => Task.FromResult(Page(_ => true, since, limit));

    private SignalPage Page(Func<JournaledSignal, bool> filter, long since, int limit)
    {
        SignalPage.CheckLimit(limit);
        var last = Journal.Count == 0 ? Floor - 1 : Journal.Max(entry => entry.Sequence);
        return new SignalPage(Floor, last, [.. Journal.Where(entry => entry.Sequence > since && filter(entry)).OrderBy(entry => entry.Sequence).Take(limit)]);
    }
}
