using DigitalBrain.Access;
using DigitalBrain.Capabilities.Storage;
using DigitalBrain.Confirmation;
using DigitalBrain.Kernel;
using DigitalBrain.Spi;

namespace DigitalBrain.Testing;

// What TestOS gives one activation: state and signals through the brain's TestJournal, timers and reminders on the TestOS clock,
// synapses through the registry, and typed proxies that carry this neuron as the caller.
internal sealed class TestNeuronContext : INeuronContext
{
    private readonly TestNeuronHost _host;

    public TestNeuronContext(TestNeuronHost host)
    {
        _host = host;
        var journal = host.OS.Journal(host.Id.Brain);
        var nudge = host.OS.Delivery.NudgeFor(host.Id.Brain);
        State = new KernelState(
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
        // An IConfirmations in TestOSOptions.Services replaces the kit's gate (a fact substitutes a faulting one), as the silo's
        // registered IConfirmations does.
        Confirmations = host.OS.Services.GetService(typeof(IConfirmations)) as IConfirmations
            ?? host.OS.Confirmations.For(ConfirmationPrincipal.App(host.Id.ToString()), Publish);
        var runner = TestOSOperations.Runner(host.OS.Services, host.Id.Brain);
        Operations = new FakeOperations(runner, Publish, (plan, ct) => StorageConfirmationGate.Check(runner, Confirmations, plan, ct));
    }

    public IConfirmations Confirmations { get; }

    public NeuronId Id => _host.Id;

    public IServiceProvider Services => _host.OS.Services;

    public TimeProvider Time => _host.OS.Clock;

    public INeuronState State { get; }

    public ITimers Timers { get; }

    public IReminders Reminders { get; }

    public IOperations Operations { get; }

    public IOperationsQuery OperationsQuery => (FakeOperations)Operations;

    public ISynapses Synapses { get; }

    public ISignalHistory History => _host.OS.Journal(Id.Brain);

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
        await ((KernelState)State).Publish(signal).ConfigureAwait(false);
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
        return _host.OS.Proxy<T>(Id, id);
    }

    public void Defer(Func<Task> work)
    {
        ArgumentNullException.ThrowIfNull(work);
        _host.Defer(work);
    }

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
