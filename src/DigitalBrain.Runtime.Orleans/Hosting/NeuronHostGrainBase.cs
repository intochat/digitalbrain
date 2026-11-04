using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
using System.Text.Json;
using DigitalBrain.Invocation;
using DigitalBrain.Rules;
using DigitalBrain.Spi;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Orleans;
using Orleans.Runtime;
using Orleans.Streams.Core;
using Orleans.Storage;
using DigitalBrain.Runtime.Orleans.Synapses;

namespace DigitalBrain.Runtime.Orleans.Hosting;

using ISynapseHost = DigitalBrain.Runtime.Orleans.Synapses.ISynapseHost;

/// <summary>
/// What the three host grains share: parse the key <c>brain/type/key</c>, resolve the plain class, build and attach it, then
/// dispatch each <see cref="RuntimeInvocation"/> through the one <see cref="NeuronBinder"/>. The invocation's Behavior is ignored:
/// provenance comes from RequestContext, stamped by <see cref="OrleansInvocationSink"/>.
/// </summary>
internal abstract partial class NeuronHostGrainBase(NeuronTypeRegistry types, IGrainFactory grains) : Grain, INeuronHost, ISynapseHost, IStreamSubscriptionObserver, IRemindable, IHostTicks
{
    private readonly Dictionary<string, IGrainTimer> _timers = new(StringComparer.Ordinal);
    /// <summary>The binder every host grain dispatches through; its cache also serves scheduling policy checks.</summary>
    internal static readonly NeuronBinder Binder = new();

    private static readonly ConcurrentDictionary<(Type Class, string Contract), Type?> Contracts = new();

    private readonly Queue<Func<Task>> _deferred = new();
    private Neuron? _neuron;
    private Type? _class;
    private string _key = string.Empty;
    private SynapseEngine? _synapses;

    /// <summary>The host grain type this class implements.</summary>
    protected abstract string GrainTypeName { get; }

    /// <inheritdoc />
    public override async Task OnActivateAsync(CancellationToken cancellationToken)
    {
        _key = this.GetPrimaryKeyString();
        if (!NeuronId.TryParse(_key, out var id)) { throw new BindingException($"'{_key}' is not a neuron id brain/type/key."); }
        if (!types.TryGetRegistration(id.Type, out var registration)
            && !(await Restore(id, cancellationToken).ConfigureAwait(true) && types.TryGetRegistration(id.Type, out registration)))
        {
            throw new BindingException($"No neuron class is hosted for type '{id.Type}'.");
        }
        var neuronClass = registration.Class;
        var expected = NeuronTypeRegistry.GrainTypeOf(registration.Policy);
        if (expected != GrainTypeName) { throw new BindingException($"{neuronClass.FullName} is hosted by {expected}, not {GrainTypeName}."); }
        _class = neuronClass;
        var neuron = Construct(ServiceProvider, neuronClass);
        var synapses = new SynapseEngine(id, this.GetGrainId(), registration, ServiceProvider, grains, types, Drain);
        var context = new HostNeuronContext(id, this.GetGrainId(), registration, ServiceProvider, grains, types,
            ServiceProvider.GetRequiredService<ILocalSiloDetails>().SiloAddress.ToString(), this, _deferred.Enqueue, synapses);
        NeuronAttachment.Attach(neuron, context);
        await NeuronAttachment.Activate(neuron, cancellationToken).ConfigureAwait(true);
        _neuron = neuron;
        _synapses = synapses;
        synapses.Neuron = neuron;
        // Load persisted synapse subscription state and catch up any sources on activation (04 D-12).
        await synapses.Load().ConfigureAwait(true);
        await synapses.CatchUp().ConfigureAwait(true);
    }

    // ActivatorUtilities caches constructors per type in a process-wide map, which would pin the load context of a generated
    // (collectible) neuron class for good; a collectible class is built from its widest public constructor with services instead.
    // Generated neuron classes are StateNeuron<TRecord>, whose single public constructor takes only services, so the choice is unambiguous.
    private static Neuron Construct(IServiceProvider services, Type neuronClass)
    {
        if (!neuronClass.IsCollectible) { return (Neuron)ActivatorUtilities.CreateInstance(services, neuronClass); }
        var constructor = neuronClass.GetConstructors().OrderByDescending(candidate => candidate.GetParameters().Length).First();
        return (Neuron)constructor.Invoke([.. constructor.GetParameters().Select(parameter => services.GetRequiredService(parameter.ParameterType))]);
    }

    // On a registration miss the host's ISynapseRestorer (none restores by default) may restore the alias once.
    private Task<bool> Restore(NeuronId id, CancellationToken cancellationToken)
        => (ServiceProvider.GetService<ISynapseRestorer>() ?? NoSynapseRestorer.Instance).TryRestore(id.Brain, id.Type, cancellationToken);

    /// <inheritdoc />
    public Task Reload()
    {
        this.DeactivateOnIdle();
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public override async Task OnDeactivateAsync(DeactivationReason reason, CancellationToken cancellationToken)
    {
        if (_neuron is { } neuron)
        {
            var cause = reason.ReasonCode switch
            {
                DeactivationReasonCode.ActivationIdle => DeactivationCause.Idle,
                DeactivationReasonCode.ShuttingDown => DeactivationCause.Shutdown,
                DeactivationReasonCode.ApplicationError or DeactivationReasonCode.ActivationFailed => DeactivationCause.Failed,
                _ => DeactivationCause.Requested,
            };
            await NeuronAttachment.Deactivate(neuron, cause, cancellationToken).ConfigureAwait(true);
        }
    }

    public Task<bool> IsInterleaved(RuntimeInvocation call)
    {
        var (neuron, bound) = Prepare(call);
        return Task.FromResult(NeutralCallMapping.MayInterleave(neuron.GetType(), bound, call));
    }

    /// <inheritdoc />
    public async Task<JsonElement?> Invoke(RuntimeInvocation call, CancellationToken cancellationToken = default)
    {
        var (neuron, bound) = Prepare(call);
        try { return await Binder.InvokeAsync(neuron, bound, call.Arguments, cancellationToken).ConfigureAwait(true); }
        catch (InconsistentStateException error)
        {
            LogStateConflict(ServiceProvider.GetRequiredService<ILogger<NeuronHostGrainBase>>(), _key,
                bound.Contract.FullName ?? bound.Contract.Name, bound.Method.Name, error.StackTrace ?? string.Empty);
            throw;
        }
        finally { await Drain().ConfigureAwait(true); }
    }

    public Task<JsonElement?> InvokeInterleaved(RuntimeInvocation call, CancellationToken cancellationToken = default)
    {
        RequireInterleaving(call);
        return Invoke(call, cancellationToken);
    }

    /// <inheritdoc />
    public async Task Post(RuntimeInvocation call)
    {
        var (neuron, bound) = Prepare(call);
        try { _ = await Binder.InvokeAsync(neuron, bound, call.Arguments, CancellationToken.None).ConfigureAwait(true); }
        finally { await Drain().ConfigureAwait(true); }
    }

    public Task PostInterleaved(RuntimeInvocation call)
    {
        RequireInterleaving(call);
        return Post(call);
    }

    /// <inheritdoc />
    public async IAsyncEnumerable<JsonElement> Stream(RuntimeInvocation call, [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        // Orleans enumerates streams through its extension outside ordinary request admission, as before.
        var (neuron, bound) = Prepare(call);
        await foreach (var item in Binder.StreamAsync(neuron, bound, call.Arguments, cancellationToken).ConfigureAwait(true)) { yield return item; }
    }

    /// <inheritdoc />
    public Task<JsonElement?> InspectState(CancellationToken cancellationToken = default)
    {
        if (_neuron is IStateInspectable inspectable)
        {
            return inspectable.InspectState(cancellationToken);
        }
        return Task.FromResult<JsonElement?>(null);
    }

    /// <inheritdoc />
    public async Task<GateVerdict> Gate(string signalAlias, JsonElement signal, Guid attempt, CancellationToken cancellationToken = default)
    {
        var receiver = Receiver();
        try
        {
            var verdict = await receiver.Gate(Decode(signalAlias, signal), attempt, cancellationToken).ConfigureAwait(true);
            return verdict switch
            {
                Check.Refused refused => new GateVerdict(false, [.. refused.Broken.Select(rule => rule.Name)], [.. refused.Broken.Select(rule => rule.Message)], null),
                // A null next state travels as null: the publisher's GateRunner fails it closed.
                Check.Allowed { Next: null } allowed => new GateVerdict(true, [], [], null) { Ticket = allowed.Ticket },
                Check.Allowed allowed => new GateVerdict(true, [], [], JsonSerializer.SerializeToElement(allowed.Next, allowed.Next.GetType(), RuntimeProtocol.OptionsFor(allowed.Next.GetType()))) { Ticket = allowed.Ticket },
                _ => throw new InvalidOperationException($"{_key} returned an unknown verdict."),
            };
        }
        finally { await Drain().ConfigureAwait(true); }
    }

    /// <inheritdoc />
    public async Task Committed(string signalAlias, JsonElement signal, JsonElement next, Guid ticket, CancellationToken cancellationToken = default)
    {
        var receiver = Receiver();
        var stateType = _class!.GetGenericArguments()[0];
        var state = next.Deserialize(stateType, RuntimeProtocol.OptionsFor(stateType)) ?? throw new JournalPayloadException($"The next state for {_key} is null.");
        try { await receiver.Committed(Decode(signalAlias, signal), new Check.Allowed(state) { Ticket = ticket }).ConfigureAwait(true); }
        finally { await Drain().ConfigureAwait(true); }
    }

    /// <inheritdoc />
    public async Task Aborted(string signalAlias, JsonElement signal, Guid ticket, CancellationToken cancellationToken = default)
    {
        var receiver = Receiver();
        try { await receiver.Aborted(Decode(signalAlias, signal), ticket).ConfigureAwait(true); }
        finally { await Drain().ConfigureAwait(true); }
    }

    private ISynapseReceiver Receiver()
        => _neuron as ISynapseReceiver ?? throw new InvalidOperationException($"The neuron {_key} is not a state neuron: it runs no gate.");

    private Signal Decode(string signalAlias, JsonElement signal)
        => ServiceProvider.GetRequiredService<JournalPayloads>().DecodeSignal(new JournaledSignalPayload(signalAlias,
            JsonElementSurrogateConverter.Utf8(signal) ?? throw new JournalPayloadException($"The signal '{signalAlias}' for {_key} is empty.")));

    private void RequireInterleaving(RuntimeInvocation call)
    {
        var (neuron, bound) = Prepare(call);
        if (!NeutralCallMapping.MayInterleave(neuron.GetType(), bound, call))
        { throw new BindingException($"{bound.Contract.FullName}.{bound.Method.Name} does not permit interleaving."); }
    }

    /// <inheritdoc />
    public async Task Admit(NeuronId subscriber, string[] signalAliases)
    {
        ArgumentNullException.ThrowIfNull(signalAliases);
        if (_neuron is not { } neuron) { throw new InvalidOperationException($"The neuron {_key} is not active."); }
        // Admission is decided under the subscriber's identity: a caller may not ask on behalf of another neuron (T-04-36).
        var caller = RequestContext.Get(HostNeuronContext.CallerKey) as string ?? string.Empty;
        if (!string.Equals(caller, subscriber.ToString(), StringComparison.Ordinal))
        {
            throw new UnauthorizedAccessException($"Admission of {subscriber} was asked by '{caller}', not by the subscriber.");
        }
        var map = ServiceProvider.GetRequiredService<JournalPayloads>().Types;
        IReadOnlyList<Type> signals = [.. signalAliases.Select(alias => map.TryResolve(alias, out var type) ? type : throw new ArgumentException($"'{alias}' is not a registered [Signal] alias.", nameof(signalAliases)))];
        // The caller stamp comes from the call's RequestContext (the trusted edge's Stamp), never from the arguments (04-19).
        await NeuronAttachment.Admit(neuron, new SynapseRequest(subscriber, signals, Capabilities.RuntimeCallerContext.Current)).ConfigureAwait(true);
    }

    /// <inheritdoc />
    public Task EnsureBrainWide(string[] signalAliases) => InstallsOnly().EnsureBrain(signalAliases);

    /// <inheritdoc />
    public Task RemoveBrainWide() => InstallsOnly().RemoveBrain();

    // Reaction edges are wired at install time: only the installs neuron of this brain asks (OrleansInstallHooks).
    private SynapseEngine InstallsOnly()
    {
        if (_synapses is not { } synapses) { throw new InvalidOperationException($"The neuron {_key} is not active."); }
        var installs = new NeuronId(NeuronId.Parse(_key).Brain, OrleansInstallHooks.InstallsType, "default").ToString();
        var caller = RequestContext.Get(HostNeuronContext.CallerKey) as string ?? string.Empty;
        if (!string.Equals(caller, installs, StringComparison.Ordinal))
        {
            throw new UnauthorizedAccessException($"The brain-wide synapse of {_key} was asked by '{caller}', not by {installs}.");
        }
        return synapses;
    }

    /// <inheritdoc />
    public Task CatchUp() => _synapses?.CatchUp() ?? Task.CompletedTask;

    /// <summary>Resumes the explicit subscription on this activation; never subscribes again (04 D-12).</summary>
    public Task OnSubscribed(IStreamSubscriptionHandleFactory handleFactory)
        => _synapses is { } synapses ? synapses.OnSubscribed(handleFactory) : Task.CompletedTask;

    /// <inheritdoc />
    public Task ReceiveReminder(string reminderName, TickStatus status) => Tick(reminderName, CancellationToken.None);

    void IHostTicks.StartTimer(string name, TimeSpan due, TimeSpan? period, bool keepAlive)
    {
        if (_timers.Remove(name, out var existing)) { existing.Dispose(); }
        _timers[name] = this.RegisterGrainTimer(static (state, ct) => state.Grain.Tick(state.Name, ct), (Grain: this, Name: name),
            new GrainTimerCreationOptions(due, period ?? Timeout.InfiniteTimeSpan) { Interleave = false, KeepAlive = keepAlive });
        if (keepAlive) { this.DelayDeactivation(due + TimeSpan.FromSeconds(5)); }
    }

    void IHostTicks.Deactivate() => this.DeactivateOnIdle();

    void IHostTicks.StopTimer(string name)
    {
        if (_timers.Remove(name, out var existing)) { existing.Dispose(); }
    }

    async Task IHostTicks.StartReminder(string name, TimeSpan due, TimeSpan period)
        => _ = await this.RegisterOrUpdateReminder(name, due, period).ConfigureAwait(true);

    async Task IHostTicks.StopReminder(string name)
    {
        if (await this.GetReminder(name).ConfigureAwait(true) is { } reminder) { await this.UnregisterReminder(reminder).ConfigureAwait(true); }
    }

    async Task<IReadOnlyList<string>> IHostTicks.ReminderNames()
        => [.. (await this.GetReminders().ConfigureAwait(true)).Select(reminder => reminder.ReminderName)];

    // A timer or reminder tick: a turn on this grain that reaches Neuron.OnReminder, then runs deferred work.
    private async Task Tick(string name, CancellationToken cancellationToken)
    {
        if (_neuron is not { } neuron) { return; }
        try { await NeuronAttachment.Remind(neuron, name, cancellationToken).ConfigureAwait(true); }
        finally { await Drain().ConfigureAwait(true); }
    }

    /// <summary>The contract <paramref name="contract"/> names on <paramref name="neuronClass"/>, or null when the class does not implement it.</summary>
    internal static Type? ContractOf(Type neuronClass, string contract)
        => Contracts.GetOrAdd((neuronClass, contract), static key => key.Class.GetInterfaces().FirstOrDefault(face => NeuronProxy.ContractName(face) == key.Contract));

    /// <summary>Binds <paramref name="call"/> against the contract it names on <paramref name="neuronClass"/>.</summary>
    /// <exception cref="BindingException">The class does not implement the contract, or the call does not bind.</exception>
    internal static BoundCall Bind(Type neuronClass, RuntimeInvocation call)
    {
        var contract = ContractOf(neuronClass, call.Contract) ?? throw new BindingException($"{neuronClass.FullName} does not implement contract {call.Contract}.");
        return Binder.Bind(contract, call);
    }

    private (Neuron Neuron, BoundCall Call) Prepare(RuntimeInvocation call)
    {
        ArgumentNullException.ThrowIfNull(call);
        if (_neuron is not { } neuron || _class is null) { throw new InvalidOperationException($"The neuron {_key} is not active."); }
        // The grain key decides brain, type and key; an invocation naming any other neuron is refused before neuron code runs.
        if (!string.Equals(call.Key, _key, StringComparison.Ordinal)) { throw new BindingException($"The call names {call.Key}, but this host runs {_key}."); }
        return (neuron, Bind(_class, call));
    }

    private async Task Drain()
    {
        while (_deferred.TryDequeue(out var work)) { await work().ConfigureAwait(true); }
    }

    [LoggerMessage(EventId = 4130, Level = LogLevel.Error,
        Message = "State conflict in {Neuron} invoking {Contract}.{Method}. Stack: {Stack}")]
    private static partial void LogStateConflict(ILogger logger, string neuron, string contract, string method, string stack);
}
