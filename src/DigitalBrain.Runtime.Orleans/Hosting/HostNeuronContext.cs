using DigitalBrain.Confirmation;
using DigitalBrain.Invocation;
using DigitalBrain.Kernel;
using DigitalBrain.Runtime.Orleans.Operations;
using DigitalBrain.Runtime.Orleans.Storage;
using DigitalBrain.Runtime.Orleans.Synapses;
using DigitalBrain.Spi;
using Microsoft.Extensions.DependencyInjection;
using Orleans;
using Orleans.Runtime;

namespace DigitalBrain.Runtime.Orleans.Hosting;

internal interface IHostNeuronContext
{
    string Caller { get; }
    string Silo { get; }
}

internal sealed class HostNeuronContext : INeuronContext, IHostNeuronContext
{
    internal const string CallerKey = "db.caller";

    private readonly IServiceProvider _services;
    private readonly IGrainFactory _grains;
    private readonly NeuronTypeRegistry _types;
    private readonly NeuronRegistration _registration;
    private readonly Action<Func<Task>> _defer;
    private readonly HostTimers _timers;
    private readonly IHostTicks _ticks;
    private readonly KernelState _state;

    public HostNeuronContext(
        NeuronId id,
        GrainId grainId,
        NeuronRegistration registration,
        IServiceProvider services,
        IGrainFactory grains,
        NeuronTypeRegistry types,
        string silo,
        IHostTicks ticks,
        Action<Func<Task>> defer,
        Delivery delivery,
        IBrainJournal journal,
        ISignalNudge nudge)
    {
        Id = id;
        Silo = silo;
        _services = services;
        _grains = grains;
        _types = types;
        _registration = registration;
        _ticks = ticks;
        _defer = defer;
        Delivery = delivery;

        Admission = new GrainAdmission(grains, types);
        var time = (TimeProvider?)services.GetService(typeof(TimeProvider)) ?? TimeProvider.System;
        Synapses = new Subscribing(id, registration.Policy == HostPolicy.Stateless, delivery, Admission, time);

        _state = new KernelState(
            id,
            registration.StateName,
            registration.Policy == HostPolicy.Stateless,
            journal,
            nudge,
            () => new Stamp(CurrentCause.Stamp().TraceId, CurrentCause.Stamp().CausationId),
            () => DeactivateOnIdle());

        _timers = new HostTimers(ticks, registration);
    }

    public NeuronId Id { get; }

    public IServiceProvider Services => _services;

    public TimeProvider Time => (TimeProvider?)_services.GetService(typeof(TimeProvider)) ?? TimeProvider.System;

    public INeuronState State => _state;

    public ITimers Timers => _timers;

    public IReminders Reminders => _timers;

    public IOperations Operations => field ??= new RuntimeOperations(_services, Id.Brain, Id.ToString());

    public IOperationsQuery OperationsQuery => field ??= new RuntimeOperationsQuery(_services, Id.Brain);

    public ISynapses Synapses { get; }

    public Delivery Delivery { get; }

    internal IAdmission Admission { get; }

    public ISignalHistory History => (_services.GetService<BrainSignalHistories>()
        ?? throw new InvalidOperationException("Signal history needs BrainLog as the cluster store (BrainStorageRegistration).")).For(Id.Brain);

    public IConfirmations Confirmations
        => _services.GetService<IConfirmationContexts>()?.For(Id.Brain, Id.ToString())
            ?? _services.GetService<IConfirmations>() ?? UnavailableConfirmations.Instance;

    public string? TraceId => CurrentCause.Stamp().TraceId;

    public string Caller => (RequestContext.Get(CallerKey) as string) ?? string.Empty;

    DigitalBrain.Access.Caller? INeuronContext.Caller => _services.GetService<DigitalBrain.Access.ICallerAmbient>()?.Current ?? Access.OrleansCallerAmbient.Instance.Current;

    public string Silo { get; }

    public Task Publish(Signal signal) => _state.Publish(signal);

    public Task PublishEphemeral(Signal signal) => _state.PublishEphemeral(signal);

    public T Get<T>(NeuronId target) where T : class
    {
        if (target.Brain != Id.Brain) { throw new ArgumentException($"Neuron {target} belongs to another brain than {Id}.", nameof(target)); }
        return NeuronProxy.Create<T>(target, new OrleansInvocationSink(_grains, _types, Id, Access.OrleansCallerAmbient.Instance));
    }

    public void Defer(Func<Task> work)
    {
        ArgumentNullException.ThrowIfNull(work);
        _defer(work);
    }

    internal void DeactivateOnIdle() => _ticks.Deactivate();

    internal string[] ValidateAliases(NeuronId from, string[] aliases)
    {
        ArgumentNullException.ThrowIfNull(aliases);
        if (_registration.Policy == HostPolicy.Stateless)
        {
            throw new InvalidOperationException($"{_registration.Class.FullName} is a [StatelessNeuron] class: stateless activations own no durable synapse.");
        }
        if (from == Id) { throw new ArgumentException("A neuron does not subscribe to itself.", nameof(from)); }
        if (aliases.Length == 0) { throw new ArgumentException("A synapse names at least one signal alias.", nameof(aliases)); }
        if (aliases.Any(string.IsNullOrWhiteSpace)) { throw new ArgumentException("A signal alias is empty.", nameof(aliases)); }
        if (from.Brain != Id.Brain) { throw new UnauthorizedAccessException($"Neuron {Id} may not subscribe to {from} of another brain."); }
        return [.. aliases.Distinct(StringComparer.Ordinal)];
    }
}
