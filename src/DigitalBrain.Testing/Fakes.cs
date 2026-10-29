using System.Collections.Concurrent;
using DigitalBrain.Capabilities;
using DigitalBrain.Capabilities.Storage;
using DigitalBrain.Spi;

namespace DigitalBrain.Testing;

/// <summary>A clock that moves only when told.</summary>
public sealed class ManualTime : TimeProvider
{
    private DateTimeOffset _now;

    /// <summary>Starts at <paramref name="start"/>, or 2026-01-01 UTC.</summary>
    public ManualTime(DateTimeOffset? start = null) => _now = start ?? new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

    /// <inheritdoc />
    public override DateTimeOffset GetUtcNow() => _now;

    /// <summary>Moves the clock forward.</summary>
    public void Advance(TimeSpan by)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(by, TimeSpan.Zero);
        _now += by;
    }
}

/// <summary>Neuron state held in memory.</summary>
public sealed class InMemoryNeuronState : INeuronState
{
    private readonly Action<IReadOnlyList<Signal>>? _saved;
    private readonly List<Signal> _signals = [];
    private object? _value;

    /// <summary>State on its own.</summary>
    public InMemoryNeuronState() { }

    internal InMemoryNeuronState(Action<IReadOnlyList<Signal>> saved) => _saved = saved;

    /// <summary>Every signal saved with a state change, in order.</summary>
    public IReadOnlyList<Signal> Signals => _signals;

    /// <summary>True when a value is stored.</summary>
    public bool Exists => _value is not null;

    /// <summary>How many times <see cref="Save{T}(T, IReadOnlyList{Signal})"/> ran.</summary>
    public int Saves { get; private set; }

    /// <inheritdoc />
    public Task<T?> Load<T>() => Task.FromResult(_value is T value ? value : default);

    /// <inheritdoc />
    public Task Save<T>(T next, IReadOnlyList<Signal> signals)
    {
        ArgumentNullException.ThrowIfNull(signals);
        _value = next;
        Saves++;
        _signals.AddRange(signals);
        _saved?.Invoke(signals);
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task Clear()
    {
        _value = null;
        return Task.CompletedTask;
    }
}

/// <summary>Timers that fire only when <see cref="Advance(TimeSpan)"/> moves their clock.</summary>
public sealed class ManualTimers : ITimers
{
    private readonly ManualTime _time;
    private readonly Dictionary<string, (DateTimeOffset Due, TimeSpan? Period)> _timers = new(StringComparer.Ordinal);
    private readonly HashSet<string> _keepAlive = new(StringComparer.Ordinal);

    /// <summary>Timers on the given clock.</summary>
    public ManualTimers(ManualTime time) => _time = time ?? throw new ArgumentNullException(nameof(time));

    /// <summary>Runs for each fired timer name, in due order.</summary>
    public Func<string, Task>? OnFire { get; set; }

    /// <summary>The names of the running timers.</summary>
    public IReadOnlyCollection<string> Names => _timers.Keys;

    /// <inheritdoc />
    public void Start(string name, TimeSpan due, TimeSpan? period) => Start(name, due, period, keepAlive: false);

    /// <inheritdoc />
    public void Start(string name, TimeSpan due, TimeSpan? period, bool keepAlive)
    {
        ArgumentException.ThrowIfNullOrEmpty(name);
        _timers[name] = (_time.GetUtcNow() + due, period);
        if (keepAlive) { _keepAlive.Add(name); } else { _keepAlive.Remove(name); }
    }

    /// <summary>The names of the running timers started with keep-alive.</summary>
    public IReadOnlyCollection<string> KeepAliveNames => _keepAlive;

    /// <inheritdoc />
    public void Stop(string name)
    {
        _timers.Remove(name);
        _keepAlive.Remove(name);
    }

    /// <summary>Moves the clock and fires every due timer; returns the fired names in order.</summary>
    public async Task<IReadOnlyList<string>> Advance(TimeSpan by)
    {
        var end = _time.GetUtcNow() + by;
        var fired = new List<string>();
        while (true)
        {
            var next = _timers.Where(timer => timer.Value.Due <= end).OrderBy(timer => timer.Value.Due).ThenBy(timer => timer.Key, StringComparer.Ordinal).FirstOrDefault();
            if (next.Key is null) { break; }
            _time.Advance(next.Value.Due - _time.GetUtcNow() is var step && step > TimeSpan.Zero ? step : TimeSpan.Zero);
            if (next.Value.Period is { } period && period > TimeSpan.Zero) { _timers[next.Key] = (next.Value.Due + period, period); }
            else { _timers.Remove(next.Key); _keepAlive.Remove(next.Key); }
            fired.Add(next.Key);
            if (OnFire is not null) { await OnFire(next.Key).ConfigureAwait(false); }
        }
        _time.Advance(end - _time.GetUtcNow());
        return fired;
    }
}

/// <summary>Reminders that fire only through <see cref="Fire(string)"/>.</summary>
public sealed class ManualReminders : IReminders
{
    private readonly Dictionary<string, (TimeSpan Due, TimeSpan Period)> _reminders = new(StringComparer.Ordinal);

    /// <summary>The neuron that receives <see cref="DigitalBrain.Neuron.OnReminder"/>.</summary>
    public Neuron? Target { get; set; }

    /// <inheritdoc />
    public Task Start(string name, TimeSpan due, TimeSpan period)
    {
        ArgumentException.ThrowIfNullOrEmpty(name);
        _reminders[name] = (due, period);
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task Stop(string name)
    {
        _reminders.Remove(name);
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<string>> Names() => Task.FromResult<IReadOnlyList<string>>([.. _reminders.Keys.Order(StringComparer.Ordinal)]);

    /// <summary>Delivers the named reminder to the attached neuron.</summary>
    /// <exception cref="InvalidOperationException">No neuron is attached.</exception>
    public Task Fire(string name, CancellationToken cancellationToken = default)
        => Target is { } neuron
            ? NeuronAttachment.Remind(neuron, name, cancellationToken)
            : throw new InvalidOperationException("No neuron is attached.");
}

/// <summary>The synapses one subscriber holds, in memory. Ensure is idempotent and refuses a self-synapse.</summary>
public sealed class RecordingSynapses : ISynapses
{
    private readonly List<Synapse> _synapses = [];

    /// <summary>Synapses held by <paramref name="subscriber"/>.</summary>
    public RecordingSynapses(NeuronId subscriber) => Subscriber = subscriber;

    /// <summary>The neuron that holds these synapses.</summary>
    public NeuronId Subscriber { get; }

    /// <summary>The current synapses.</summary>
    public IReadOnlyList<Synapse> Current => _synapses;

    /// <inheritdoc />
    public Task Ensure<TSignal>(NeuronId from) where TSignal : Signal => Ensure(from, typeof(TSignal));

    /// <inheritdoc />
    public Task Ensure(NeuronId from, params Type[] signals)
    {
        ArgumentNullException.ThrowIfNull(signals);
        if (from == Subscriber) { throw new ArgumentException("A neuron does not subscribe to itself.", nameof(from)); }
        foreach (var signal in signals)
        {
            if (signal is null || !typeof(Signal).IsAssignableFrom(signal)) { throw new ArgumentException($"{signal} is not a signal type.", nameof(signals)); }
            var synapse = new Synapse(from, signal, Subscriber);
            if (!_synapses.Contains(synapse)) { _synapses.Add(synapse); }
        }
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task Remove(NeuronId from)
    {
        _synapses.RemoveAll(synapse => synapse.From == from);
        return Task.CompletedTask;
    }
}

/// <summary>Signal history held in memory. <see cref="Trim(long)"/> raises the floor.</summary>
public sealed class InMemorySignalHistory : ISignalHistory
{
    private readonly List<JournaledSignal> _entries = [];
    private long _floor = 1;

    /// <summary>Journals a signal with the next sequence and returns that sequence.</summary>
    public long Append(NeuronId publisher, Signal signal, DateTimeOffset at)
    {
        ArgumentNullException.ThrowIfNull(signal);
        var sequence = Last + 1;
        _entries.Add(new JournaledSignal(publisher, sequence, signal, at));
        return sequence;
    }

    /// <summary>The highest sequence written.</summary>
    public long Last => _entries.Count == 0 ? _floor - 1 : _entries[^1].Sequence;

    /// <summary>Drops entries below <paramref name="floor"/>.</summary>
    public void Trim(long floor)
    {
        _floor = Math.Max(_floor, floor);
        _entries.RemoveAll(entry => entry.Sequence < _floor);
    }

    /// <inheritdoc />
    public Task<SignalPage> Read(NeuronId publisher, long since, int limit, CancellationToken ct)
        => Task.FromResult(Page(entry => entry.Publisher == publisher, since, limit));

    /// <inheritdoc />
    public Task<SignalPage> Read(long since, int limit, CancellationToken ct)
        => Task.FromResult(Page(_ => true, since, limit));

    private SignalPage Page(Func<JournaledSignal, bool> filter, long since, int limit)
    {
        SignalPage.CheckLimit(limit);
        return new SignalPage(_floor, Last, [.. _entries.Where(entry => entry.Sequence > since && filter(entry)).Take(limit)]);
    }
}

/// <summary>
/// Operations held in memory: every started plan, its progress, result and cancellation. Built with a
/// <see cref="StorageWriteRunner"/> and a publish callback (as <see cref="TestOS"/> does for every neuron) it has the runtime
/// semantics (D-04): a <see cref="WritePlan"/> runs through the same runner the runtime operation entity uses (dry run, conflict
/// fail by default, copy-verify-delete between connections) and <see cref="OperationStarted"/>, <see cref="OperationProgress"/>
/// and <see cref="OperationDone"/> are published in order. In TestOS the write runs inside <see cref="Start"/>, in the caller's turn.
/// </summary>
public sealed class FakeOperations : IOperations
{
    private readonly ConcurrentDictionary<DigitalBrain.Operations.OperationId, FakeOperation> _operations = new();
    private readonly StorageWriteRunner? _runner;
    private readonly Func<Signal, Task>? _publish;
    private readonly Func<WritePlan, CancellationToken, Task<string?>>? _gate;
    private int _next;

    /// <summary>Operations that only record: the caller drives every plan through its handle.</summary>
    public FakeOperations()
    {
    }

    /// <summary>Operations that run write plans through <paramref name="runner"/> and publish operation signals with <paramref name="publish"/>.</summary>
    public FakeOperations(StorageWriteRunner runner, Func<Signal, Task> publish)
        : this(runner, publish, null)
    {
    }

    /// <summary>
    /// Operations as above that first ask <paramref name="gate"/>, as the runtime asks its operation gate: a non-null answer is the
    /// refusal reason and the operation ends <see cref="DigitalBrain.Operations.OperationResult.Failed"/> with code <c>denied</c>.
    /// </summary>
    public FakeOperations(StorageWriteRunner runner, Func<Signal, Task> publish, Func<WritePlan, CancellationToken, Task<string?>>? gate)
    {
        ArgumentNullException.ThrowIfNull(runner);
        ArgumentNullException.ThrowIfNull(publish);
        _runner = runner;
        _publish = publish;
        _gate = gate;
    }

    /// <summary>Every started operation by id.</summary>
    public IReadOnlyDictionary<DigitalBrain.Operations.OperationId, FakeOperation> All => _operations;

    /// <inheritdoc />
    public async Task<IOperation> Start(DigitalBrain.Operations.OperationPlan plan, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(plan);
        var write = plan as WritePlan;
        var operation = new FakeOperation(new DigitalBrain.Operations.OperationId($"op-{Interlocked.Increment(ref _next)}"), plan, _publish, write is not null && _runner is not null);
        _operations[operation.Id] = operation;
        if (write is not null && _publish is not null)
        {
            // As on the runtime: inline bytes never travel in the started signal.
            await _publish(new OperationStarted(operation.Id, write with { Inline = null })).ConfigureAwait(false);
        }
        if (write is not null && _gate is not null && await _gate(write, ct).ConfigureAwait(false) is { } refused)
        {
            await operation.Complete(new DigitalBrain.Operations.OperationResult.Failed("denied", refused)).ConfigureAwait(false);
            return operation;
        }
        if (write is not null && _runner is not null)
        {
            await operation.Complete(await _runner.Run(write, operation).ConfigureAwait(false)).ConfigureAwait(false);
        }
        return operation;
    }

    /// <inheritdoc />
    /// <exception cref="KeyNotFoundException">No operation has this id.</exception>
    public Task Cancel(DigitalBrain.Operations.OperationId id, CancellationToken ct)
        => _operations.TryGetValue(id, out var operation) ? operation.Cancel() : throw new KeyNotFoundException($"No operation {id}.");
}

/// <summary>An operation held in memory.</summary>
public sealed class FakeOperation : IOperation, IDisposable
{
    private readonly CancellationTokenSource _cancellation = new();
    private readonly List<double> _progress = [];
    private readonly TaskCompletionSource<DigitalBrain.Operations.OperationResult> _done = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly Func<Signal, Task>? _publish;
    private readonly bool _driven;

    internal FakeOperation(DigitalBrain.Operations.OperationId id, DigitalBrain.Operations.OperationPlan plan, Func<Signal, Task>? publish = null, bool driven = false)
    {
        Id = id;
        Plan = plan;
        _publish = publish;
        _driven = driven;
    }

    /// <inheritdoc />
    public DigitalBrain.Operations.OperationId Id { get; }

    /// <summary>The plan the operation was started with.</summary>
    public DigitalBrain.Operations.OperationPlan Plan { get; }

    /// <summary>Every reported progress fraction, in order; reports after a cancel or after completion are dropped.</summary>
    public IReadOnlyList<double> Progress => _progress;

    /// <summary>The result, or null while running.</summary>
    public DigitalBrain.Operations.OperationResult? Result { get; private set; }

    /// <summary>Completes with the result once the operation ended.</summary>
    public Task<DigitalBrain.Operations.OperationResult> Completion => _done.Task;

    /// <inheritdoc />
    public CancellationToken Cancellation => _cancellation.Token;

    /// <inheritdoc />
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="fraction"/> is outside 0..1.</exception>
    public async ValueTask Report(double fraction)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(fraction, 0d);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(fraction, 1d);
        if (Result is not null || _cancellation.IsCancellationRequested) { return; }
        _progress.Add(fraction);
        if (_publish is not null) { await _publish(new OperationProgress(Id, fraction)).ConfigureAwait(false); }
    }

    /// <inheritdoc />
    /// <remarks>Once only, as on the runtime: later calls are ignored.</remarks>
    public async ValueTask Complete(DigitalBrain.Operations.OperationResult result)
    {
        ArgumentNullException.ThrowIfNull(result);
        if (Result is not null) { return; }
        Result = result;
        if (_publish is not null) { await _publish(new OperationDone(Id, result)).ConfigureAwait(false); }
        _done.TrySetResult(result);
    }

    // Fires the token; an operation its caller drives also ends Cancelled now, a write the runner drives ends when the runner stops.
    internal async Task Cancel()
    {
        await _cancellation.CancelAsync().ConfigureAwait(false);
        if (!_driven) { await Complete(new DigitalBrain.Operations.OperationResult.Cancelled()).ConfigureAwait(false); }
    }

    /// <inheritdoc />
    public void Dispose() => _cancellation.Dispose();
}

/// <summary>Neuron proxies registered by hand.</summary>
public sealed class FakeNeuronProxies : INeuronProxies
{
    private readonly Dictionary<(NeuronId, Type), object> _proxies = [];

    /// <summary>Makes <see cref="Get{T}(NeuronId)"/> return <paramref name="instance"/> for this id.</summary>
    public FakeNeuronProxies Register<T>(NeuronId id, T instance) where T : class
    {
        ArgumentNullException.ThrowIfNull(instance);
        _proxies[(id, typeof(T))] = instance;
        return this;
    }

    /// <inheritdoc />
    /// <exception cref="KeyNotFoundException">No proxy is registered for the id.</exception>
    public T Get<T>(NeuronId id) where T : class
        => _proxies.TryGetValue((id, typeof(T)), out var proxy) ? (T)proxy
            : throw new KeyNotFoundException($"No {typeof(T).Name} is registered for neuron {id}.");
}

/// <summary>Capabilities registered by hand, one or more connections per capability.</summary>
public sealed class FakeCapabilityBroker : ICapabilityBroker
{
    private readonly Dictionary<Type, List<object>> _connections = [];

    /// <summary>Adds <paramref name="instance"/> as a connection of <typeparamref name="TCapability"/> for every brain.</summary>
    /// <param name="instance">The implementation.</param>
    /// <param name="connectionKey">The connection key; defaults to the capability type name plus position.</param>
    /// <param name="label">The connection label; defaults to the key.</param>
    public FakeCapabilityBroker Register<TCapability>(TCapability instance, string? connectionKey = null, string? label = null) where TCapability : class
    {
        ArgumentNullException.ThrowIfNull(instance);
        if (!_connections.TryGetValue(typeof(TCapability), out var list)) { _connections[typeof(TCapability)] = list = []; }
        var key = connectionKey ?? $"{typeof(TCapability).Name}-{list.Count + 1}";
        list.Add(new CapabilityConnection<TCapability>(key, label ?? key, instance));
        return this;
    }

    /// <inheritdoc />
    public Task<TCapability> Resolve<TCapability>(BrainId brain, CancellationToken ct) where TCapability : class
    {
        var all = Connections<TCapability>();
        return all.Count == 1
            ? Task.FromResult(all[0].Capability)
            : throw new InvalidOperationException($"{typeof(TCapability).Name} has {all.Count} connections; Resolve needs exactly one, use ResolveAll.");
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<CapabilityConnection<TCapability>>> ResolveAll<TCapability>(BrainId brain, CancellationToken ct) where TCapability : class
        => Task.FromResult(Connections<TCapability>());

    private IReadOnlyList<CapabilityConnection<TCapability>> Connections<TCapability>() where TCapability : class
        => _connections.TryGetValue(typeof(TCapability), out var list) ? [.. list.Cast<CapabilityConnection<TCapability>>()] : [];
}

/// <summary>A neuron context made of fakes. Published signals are recorded.</summary>
public sealed class FakeNeuronContext : INeuronContext
{
    private readonly List<Signal> _published = [];
    private readonly List<Func<Task>> _deferred = [];

    /// <summary>A context for the neuron with this id.</summary>
    public FakeNeuronContext(NeuronId id, IServiceProvider? services = null)
    {
        Id = id;
        MemoryState = new InMemoryNeuronState(_published.AddRange);
        Synapses = new RecordingSynapses(id);
        Services = services ?? EmptyServices.Instance;
        Timers = new ManualTimers(Clock);
    }

    /// <inheritdoc />
    public NeuronId Id { get; }

    /// <inheritdoc />
    public IServiceProvider Services { get; }

    /// <summary>The manual clock behind <see cref="Time"/>.</summary>
    public ManualTime Clock { get; } = new();

    /// <inheritdoc />
    public TimeProvider Time => Clock;

    /// <summary>The in-memory state.</summary>
    public InMemoryNeuronState MemoryState { get; }

    /// <inheritdoc />
    public INeuronState State => MemoryState;

    /// <summary>The manual timers.</summary>
    public ManualTimers Timers { get; }

    ITimers INeuronContext.Timers => Timers;

    /// <summary>The manual reminders.</summary>
    public ManualReminders Reminders { get; } = new();

    IReminders INeuronContext.Reminders => Reminders;

    /// <summary>The fake operations.</summary>
    public FakeOperations Operations { get; } = new();

    IOperations INeuronContext.Operations => Operations;

    /// <summary>The recording synapses.</summary>
    public RecordingSynapses Synapses { get; }

    ISynapses INeuronContext.Synapses => Synapses;

    /// <summary>The in-memory signal history.</summary>
    public InMemorySignalHistory History { get; } = new();

    ISignalHistory INeuronContext.History => History;

    /// <summary>The confirmation gate on <see cref="Clock"/>; this neuron asks as an app and its signals are recorded in <see cref="Published"/>.</summary>
    public TestConfirmations Confirmations => field ??= new TestConfirmations(Clock);

    DigitalBrain.Confirmation.IConfirmations INeuronContext.Confirmations
        => field ??= Confirmations.For(DigitalBrain.Confirmation.ConfirmationPrincipal.App(Id.ToString()), Publish);

    /// <summary>The proxies <see cref="Get{T}(NeuronId)"/> returns.</summary>
    public FakeNeuronProxies Proxies { get; } = new();

    /// <summary>Every signal published, alone or with a state change, in order.</summary>
    public IReadOnlyList<Signal> Published => _published;

    /// <inheritdoc />
    public Task Publish(Signal signal)
    {
        ArgumentNullException.ThrowIfNull(signal);
        _published.Add(signal);
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public T Get<T>(NeuronId id) where T : class => Proxies.Get<T>(id);

    /// <inheritdoc />
    public void Defer(Func<Task> work)
    {
        ArgumentNullException.ThrowIfNull(work);
        _deferred.Add(work);
    }

    /// <summary>Runs the deferred work in order, as a runtime does when a turn ends.</summary>
    public async Task RunDeferred()
    {
        var work = _deferred.ToList();
        _deferred.Clear();
        foreach (var item in work) { await item().ConfigureAwait(false); }
    }

    private sealed class EmptyServices : IServiceProvider
    {
        internal static readonly EmptyServices Instance = new();

        public object? GetService(Type serviceType) => null;
    }
}
