using System.Collections.Concurrent;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text.Json;
using DigitalBrain.Invocation;
using DigitalBrain.Spi;

namespace DigitalBrain.Testing;

// One neuron id in TestOS: its activation, its turn and its timers and reminders. Seeded from the os in-memory NeuronHost.
// Calls are bound by NeuronBinder against the activated instance and run as turns; a one-way call is queued behind the turn and
// not awaited; an awaited call fails at TestOSOptions.CallTimeout on the TestOS clock, so an A -> B -> A cycle fails instead of hanging.
internal sealed class TestNeuronHost : IDisposable
{
    private static readonly AsyncLocal<List<Func<Task>>?> Deferred = new();
    private static readonly ConcurrentDictionary<(Type Class, string Contract), Type?> Contracts = new();

    private readonly TestOS _os;
    private readonly TurnScheduler _turns = new();
    private readonly SemaphoreSlim _activating = new(1, 1);
    private readonly Lock _gate = new();
    private readonly Dictionary<string, ITimer> _timers = new(StringComparer.Ordinal);
    private readonly HashSet<string> _keepAlive = new(StringComparer.Ordinal);
    private readonly Dictionary<string, ITimer> _reminders = new(StringComparer.Ordinal);
    private Neuron? _neuron;

    public TestNeuronHost(TestOS os, NeuronId id, Type neuronClass)
    {
        _os = os;
        Id = id;
        Class = neuronClass;
        Created = os.Clock.GetUtcNow();
    }

    public NeuronId Id { get; }

    public Type Class { get; }

    public DateTimeOffset Created { get; }

    public bool IsActive => _neuron is not null;

    public TestOS OS => _os;

    public Task<JsonElement?> Invoke(RuntimeInvocation call, CancellationToken cancellationToken)
    {
        var (bound, interleave, _) = Bind(call);
        var work = _os.Track(Turn(interleave, neuron => _os.Binder.InvokeAsync(neuron, bound, call.Arguments, cancellationToken)));
        return work.WaitAsync(_os.Options.CallTimeout, _os.Clock, cancellationToken);
    }

    public Task Post(RuntimeInvocation call)
    {
        var (bound, interleave, _) = Bind(call);
        _ = _os.Track(Observe(Turn(interleave, neuron => _os.Binder.InvokeAsync(neuron, bound, call.Arguments, CancellationToken.None))));
        return Task.CompletedTask;
    }

    // A stream's items are produced outside the turn: the call interleaves, as a streaming host call does.
    public async IAsyncEnumerable<JsonElement> Stream(RuntimeInvocation call, [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var (bound, _, _) = Bind(call);
        var neuron = await Activated().ConfigureAwait(false);
        await foreach (var item in _os.Binder.StreamAsync(neuron, bound, call.Arguments, cancellationToken).ConfigureAwait(false))
        {
            yield return item;
        }
    }

    public Task Activate() => Turn(interleave: false, _ => Task.FromResult(0));

    public Task Deactivate(DeactivationCause cause) => _turns.Run(interleave: false, async () =>
    {
        await DeactivateNow(cause).ConfigureAwait(false);
        return 0;
    });

    // Runs body as a turn of this neuron, activating it first. Work deferred inside the turn runs once the turn has ended.
    public async Task<T> Turn<T>(bool interleave, Func<Neuron, Task<T>> body)
    {
        var deferred = new List<Func<Task>>();
        try
        {
            return await _turns.Run(interleave, async () =>
            {
                Deferred.Value = deferred;
                var neuron = await Activated().ConfigureAwait(false);
                return await body(neuron).ConfigureAwait(false);
            }).ConfigureAwait(false);
        }
        finally
        {
            if (deferred.Count > 0) { _ = _os.Track(Observe(RunDeferred(deferred))); }
        }
    }

    public void Defer(Func<Task> work)
    {
        if (Deferred.Value is { } turn) { turn.Add(work); }
        else { _ = _os.Track(Observe(RunDeferred([work]))); }
    }

    // TestOS never collects an activation on idle, so keep-alive is only recorded (04-18).
    public bool IsKeptAlive(string name)
    {
        lock (_gate) { return _keepAlive.Contains(name); }
    }

    public void StartTimer(string name, TimeSpan due, TimeSpan? period, bool keepAlive = false)
    {
        ArgumentException.ThrowIfNullOrEmpty(name);
        var timer = _os.Clock.CreateTimer(_ => Tick(name, reminder: false), null, Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
        lock (_gate)
        {
            if (_timers.Remove(name, out var previous)) { previous.Dispose(); }
            _timers[name] = timer;
            if (keepAlive) { _keepAlive.Add(name); } else { _keepAlive.Remove(name); }
        }
        timer.Change(due, period ?? Timeout.InfiniteTimeSpan);
    }

    public void StopTimer(string name)
    {
        lock (_gate)
        {
            if (_timers.Remove(name, out var timer)) { timer.Dispose(); }
            _keepAlive.Remove(name);
        }
    }

    public void StartReminder(string name, TimeSpan due, TimeSpan period)
    {
        ArgumentException.ThrowIfNullOrEmpty(name);
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(period, TimeSpan.Zero);
        var timer = _os.Clock.CreateTimer(_ => Tick(name, reminder: true), null, Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
        lock (_gate)
        {
            if (_reminders.Remove(name, out var previous)) { previous.Dispose(); }
            _reminders[name] = timer;
        }
        timer.Change(due, period);
    }

    public void StopReminder(string name)
    {
        lock (_gate)
        {
            if (_reminders.Remove(name, out var timer)) { timer.Dispose(); }
        }
    }

    public IReadOnlyList<string> ReminderNames()
    {
        lock (_gate) { return [.. _reminders.Keys.Order(StringComparer.Ordinal)]; }
    }

    public void Dispose()
    {
        lock (_gate)
        {
            foreach (var timer in _timers.Values.Concat(_reminders.Values)) { timer.Dispose(); }
            _timers.Clear();
            _reminders.Clear();
        }
        _turns.Dispose();
        _activating.Dispose();
    }

    // A timer tick needs the activation it lives with; a reminder reactivates the neuron. Both run as turns and reach
    // Neuron.OnReminder(name).
    private void Tick(string name, bool reminder)
    {
        if (!reminder && !IsActive) { return; }
        _ = _os.Track(Observe(Turn(interleave: false, async neuron =>
        {
            await NeuronAttachment.Remind(neuron, name, CancellationToken.None).ConfigureAwait(false);
            return 0;
        })));
    }

    private (BoundCall Bound, bool Interleave, bool OneWay) Bind(RuntimeInvocation call)
    {
        var contract = Contracts.GetOrAdd((Class, call.Contract), static key => key.Class.GetInterfaces().FirstOrDefault(face => NeuronProxy.ContractName(face) == key.Contract))
            ?? throw new BindingException($"{Class.FullName} does not implement contract {call.Contract}.");
        var bound = _os.Binder.Bind(contract, call);
        var (interleave, oneWay) = CallMarkers.Of(Class, bound.Method, call);
        return (bound, interleave, oneWay);
    }

    private async Task<Neuron> Activated()
    {
        if (_neuron is { } active) { return active; }
        await _activating.WaitAsync().ConfigureAwait(false);
        try
        {
            if (_neuron is { } raced) { return raced; }
            var neuron = Construct();
            NeuronAttachment.Attach(neuron, new TestNeuronContext(this));
            await NeuronAttachment.Activate(neuron, CancellationToken.None).ConfigureAwait(false);
            _neuron = neuron;
            _os.Synapses.Activated(this);
            return neuron;
        }
        finally { _activating.Release(); }
    }

    private async Task DeactivateNow(DeactivationCause cause)
    {
        if (_neuron is not { } neuron) { return; }
        _neuron = null;
        lock (_gate)
        {
            foreach (var timer in _timers.Values) { timer.Dispose(); }
            _timers.Clear();
        }
        await NeuronAttachment.Deactivate(neuron, cause, CancellationToken.None).ConfigureAwait(false);
    }

    // The widest public constructor, its parameters resolved from the TestOS services.
    private Neuron Construct()
    {
        var constructor = Class.GetConstructors().OrderByDescending(candidate => candidate.GetParameters().Length).FirstOrDefault()
            ?? throw new InvalidOperationException($"{Class.FullName} has no public constructor.");
        var arguments = constructor.GetParameters().Select(parameter => _os.Services.GetService(parameter.ParameterType)
            ?? (parameter.HasDefaultValue ? parameter.DefaultValue
                : throw new InvalidOperationException($"{Class.FullName} needs a {parameter.ParameterType.Name} that TestOSOptions.Services does not provide."))).ToArray();
        try { return (Neuron)constructor.Invoke(arguments); }
        catch (TargetInvocationException error) when (error.InnerException is not null)
        {
            System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(error.InnerException).Throw();
            throw;
        }
    }

    private static async Task RunDeferred(List<Func<Task>> work)
    {
        await Task.Yield();
        foreach (var item in work) { await item().ConfigureAwait(false); }
    }

    private async Task Observe(Task work)
    {
        try { await work.ConfigureAwait(false); }
        catch (Exception error) when (error is not OutOfMemoryException) { _os.Fail(error); }
    }
}
