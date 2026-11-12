using DigitalBrain.Access;
using DigitalBrain.Capabilities.Storage;
using DigitalBrain.Confirmation;
using DigitalBrain.Kernel;
using DigitalBrain.Neurons;
using DigitalBrain.Operations;

namespace DigitalBrain.Testing;

// What TestOS gives one activation: state and signals through the brain's TestJournal, timers and reminders on the TestOS clock,
// synapses through the registry, and typed proxies that carry this neuron as the caller.
internal sealed class TestNeuronContext : INeuronContext, INeuronSeat
{
    private readonly TestNeuronHost _host;
    private readonly KernelState _state;

    public TestNeuronContext(TestNeuronHost host)
    {
        _host = host;
        var journal = host.OS.Journal(host.Id.Brain);
        var nudge = host.OS.Delivery.NudgeFor(host.Id.Brain);
        _state = new KernelState(
            host.Id,
            "state",
            host.Stateless,
            journal,
            nudge,
            () => { var (t, c) = TestCause.Stamp(); return new Stamp(t, c); },
            () => host.Drop());
        Timers = new HostTimers(host);
        Reminders = new HostReminders(host);
        Synapses = new Subscribing(host.Id, host.Stateless, host.OS.Delivery.For(host.Id), host.OS.Delivery.Admission, host.OS.Clock);

        var runner = TestOSOperations.Runner(host.OS.Services, host.Id.Brain);
        var confirmations = host.OS.Services.GetService(typeof(IConfirmations)) as IConfirmations
            ?? host.OS.Confirmations.For(ConfirmationPrincipal.App(host.Id.ToString()), Publish);
        var operations = new FakeOperations(runner, Publish, confirmations);

        NeuronBinding[] bindings =
        [
            new(typeof(IOperations), (_, _) => operations),
            new(typeof(IOperationsQuery), (_, _) => operations),
            new(typeof(FakeOperations), (_, _) => operations),
            new(typeof(IConfirmations), (_, _) => confirmations),
            new(typeof(ISignalHistory), (_, id) => host.OS.Journal(id.Brain)),
        ];
        Services = new NeuronServices(host.OS.Services, host.Id, bindings);
    }

    public NeuronId Id => _host.Id;

    public IServiceProvider Services { get; }

    public TimeProvider Time => _host.OS.Clock;

    public ITimers Timers { get; }

    public IReminders Reminders { get; }

    public ISynapses Synapses { get; }

    public string? TraceId => TestCause.Stamp().TraceId;

    public Caller? Caller => TestCallerAmbient.Instance.Current;

    public async Task Publish(Signal signal)
    {
        ArgumentNullException.ThrowIfNull(signal);
        if (signal.GetType().IsDefined(typeof(EphemeralSignalAttribute), inherit: false))
        {
            await PublishEphemeral(signal).ConfigureAwait(false);
            return;
        }
        await _state.Publish(signal).ConfigureAwait(false);
    }

    public Task PublishEphemeral(Signal signal)
    {
        ArgumentNullException.ThrowIfNull(signal);
        _host.OS.Listeners.Offered(Id, [signal]);
        return Task.CompletedTask;
    }

    public T Get<T>(NeuronId id) where T : class
    {
        if (id.Brain != Id.Brain) { throw new ArgumentException($"Neuron {id} belongs to another brain than {Id}.", nameof(id)); }
        if (id == DigitalBrain.Confirmation.IConfirmationRequests.Of(Id.Brain))
        {
            if (typeof(T) == typeof(DigitalBrain.Confirmation.IConfirmationRequests)) { return (T)(object)new BrainConfirmationRequests(_host.OS.Confirmations, _host.OS, Id.Brain); }
            if (typeof(T) == typeof(DigitalBrain.Confirmation.IConfirmationInbox)) { return (T)(object)_host.OS.Confirmations; }
        }
        return _host.OS.Proxy<T>(Id, id);
    }

    public void Defer(Func<Task> work)
    {
        ArgumentNullException.ThrowIfNull(work);
        _host.Defer(work);
    }

    public void Detach(Func<CancellationToken, Task> work)
    {
        ArgumentNullException.ThrowIfNull(work);
        _host.OS.Track(Task.Run(async () =>
        {
            try
            {
                await work(CancellationToken.None).ConfigureAwait(false);
            }
            catch
            {
                // Detached work failures
            }
        }));
    }

    public Task<T?> Load<T>() where T : class => _state.Load<T>();

    public Task Save<T>(T next, IReadOnlyList<Signal> signals) where T : class => _state.Save(next, signals);

    public Task Clear() => _state.Clear();

    private sealed class HostTimers(TestNeuronHost host) : ITimers
    {
        public void Start(string name, TimeSpan due, TimeSpan? period) => host.StartTimer(name, due, period, keepAlive: false);

        public void Start(string name, TimeSpan due, TimeSpan? period, bool keepAlive) => host.StartTimer(name, due, period, keepAlive);

        public void Stop(string name) => host.StopTimer(name);
    }

    private sealed class HostReminders(TestNeuronHost host) : IReminders
    {
        public Task Start(string name, TimeSpan due, TimeSpan period)
        {
            host.StartReminder(name, due, period);
            return Task.CompletedTask;
        }

        public Task Stop(string name)
        {
            host.StopReminder(name);
            return Task.CompletedTask;
        }

        public Task<IReadOnlyList<string>> Names() => Task.FromResult(host.ReminderNames());
    }
}
