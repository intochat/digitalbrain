using DigitalBrain.Capabilities.Storage;
using DigitalBrain.Confirmation;
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
        State = new JournalState(host);
        Timers = new HostTimers(host);
        Reminders = new HostReminders(host);
        Synapses = new TestSynapses(host);
        Confirmations = host.OS.Confirmations.For(ConfirmationPrincipal.App(host.Id.ToString()), Publish);
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

    public ISynapses Synapses { get; }

    public ISignalHistory History => _host.OS.Journal(Id.Brain);

    public Task Publish(Signal signal)
    {
        ArgumentNullException.ThrowIfNull(signal);
        if (signal.GetType().IsDefined(typeof(EphemeralSignalAttribute), inherit: false))
        {
            return PublishEphemeral(signal);
        }
        var written = _host.OS.Journal(Id.Brain).Append(Id, [signal]);
        _host.OS.Synapses.Committed(Id);
        _host.OS.Synapses.Appended(Id, written);
        return Task.CompletedTask;
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

    private sealed class JournalState(TestNeuronHost host) : INeuronState
    {
        public Task<T?> Load<T>() => Task.FromResult(host.OS.Journal(host.Id.Brain).Load<T>(host.Id));

        public Task Save<T>(T next, IReadOnlyList<Signal> signals)
        {
            ArgumentNullException.ThrowIfNull(signals);
            var written = host.OS.Journal(host.Id.Brain).Save(host.Id, next, signals);
            host.OS.Synapses.Committed(host.Id);
            host.OS.Synapses.Appended(host.Id, written);
            return Task.CompletedTask;
        }

        public Task Clear()
        {
            host.OS.Journal(host.Id.Brain).Clear(host.Id);
            return Task.CompletedTask;
        }
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
