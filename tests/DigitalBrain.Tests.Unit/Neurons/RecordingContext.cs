using DigitalBrain.Confirmation;
using DigitalBrain.Kernel;
using DigitalBrain.Neurons;

namespace DigitalBrain.Tests.Unit.Neurons;

// Records what a neuron asks of its runtime: state writes, publishes and Get calls.
internal sealed class RecordingContext(NeuronId id) : INeuronContext, INeuronSeat, ISynapses, ISignalHistory
{
    private object? _stored;

    public List<(object State, IReadOnlyList<Signal> Signals)> Writes { get; } = [];

    public List<(object State, IReadOnlyList<Signal> Signals)> ViewWrites { get; } = [];

    public List<Signal> Published { get; } = [];

    public List<NeuronId> Gets { get; } = [];

    public Dictionary<NeuronId, object> Neurons { get; } = [];

    public List<Func<Task>> Deferred { get; } = [];

    public NeuronId Id { get; } = id;

    public IServiceProvider? RecordedServices { get; set; }

    public IServiceProvider Services => RecordedServices ?? new RecordingServiceProvider(this);

    public TimeProvider Time { get; set; } = TimeProvider.System;

    public ITimers? RecordedTimers { get; set; }

    public ITimers Timers => RecordedTimers ?? throw new NotSupportedException();

    public IReminders? RecordedReminders { get; set; }

    public IReminders Reminders => RecordedReminders ?? throw new NotSupportedException();

    public IConfirmations? RecordedConfirmations { get; set; }

    public ISynapses Synapses => this;

    public ISignalHistory History => this;

    public string? TraceId { get; set; }

    public DigitalBrain.Access.Caller? Caller => null;

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

    public bool Interrupts(Exception error)
    {
        for (var current = error; current is not null; current = current.InnerException)
        {
            if (current.GetType().FullName == "Orleans.Runtime.SiloUnavailableException") { return true; }
        }
        var stopping = (RecordedServices?.GetService(typeof(Microsoft.Extensions.Hosting.IHostApplicationLifetime)) as Microsoft.Extensions.Hosting.IHostApplicationLifetime)?.ApplicationStopping;
        return stopping?.IsCancellationRequested == true;
    }

    /// <summary>When set, <see cref="Load{T}"/> completes later on a thread-pool thread, as a real store's read can.</summary>
    public bool LoadsOffThread { get; set; }

    public Task<T?> Load<T>() where T : class
    {
        T? value = default;
        if (_stored is T stored)
        {
            value = stored;
        }
        else if (_stored is byte[] utf8)
        {
            value = System.Text.Json.JsonSerializer.Deserialize<T>(utf8, DigitalBrain.Invocation.RuntimeProtocol.OptionsFor(typeof(T)));
        }

        return LoadsOffThread ? Task.Run(async () => { await Task.Delay(20); return value; }) : Task.FromResult(value);
    }

    public Task Save<T>(T next, IReadOnlyList<Signal> signals) where T : class
    {
        // A unit neuron stores its record with its view beside it: the record is what a write is about. A save that only journals a
        // stored view (ViewChanged) is recorded in ViewWrites, so the facts about state writes count state changes only.
        _stored = next;
        object state = next is { } value && value.GetType() is { IsGenericType: true } type && type.GetGenericTypeDefinition() == typeof(DigitalBrain.Units.StoredUnit<>)
            ? type.GetProperty(nameof(DigitalBrain.Units.StoredUnit<object>.State))!.GetValue(value)!
            : next!;
        if (signals.Count > 0 && signals.All(signal => signal is ViewChanged)) { ViewWrites.Add((state, [.. signals])); }
        else { Writes.Add((state, [.. signals])); }
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

    public Task EnsureBrainWide(params Type[] signals) => Task.CompletedTask;

    public Task EnsureBrainWide(IReadOnlyList<string> signalAliases) => Task.CompletedTask;

    public Task RemoveBrainWide() => Task.CompletedTask;

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

    public Task<SignalPage> ReadTrace(string traceId, long since, int limit, CancellationToken ct)
        => string.IsNullOrEmpty(traceId) ? throw new ArgumentException("A trace id is required.", nameof(traceId)) : Task.FromResult(Page(entry => entry.TraceId == traceId, since, limit));

    private SignalPage Page(Func<JournaledSignal, bool> filter, long since, int limit)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(limit, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(limit, 1000);
        var last = Journal.Count == 0 ? Floor - 1 : Journal.Max(entry => entry.Sequence);
        return new SignalPage(Floor, last, [.. Journal.Where(entry => entry.Sequence > since && filter(entry)).OrderBy(entry => entry.Sequence).Take(limit)]);
    }

    private sealed class RecordingServiceProvider(RecordingContext context) : IServiceProvider
    {
        public object? GetService(Type serviceType)
        {
            if (serviceType == typeof(IConfirmations)) { return context.RecordedConfirmations; }
            if (serviceType == typeof(ISignalHistory)) { return context; }
            return null;
        }
    }
}
