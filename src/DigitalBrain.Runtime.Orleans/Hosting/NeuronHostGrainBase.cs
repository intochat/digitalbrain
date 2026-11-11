using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
using System.Text.Json;
using DigitalBrain.Access;
using DigitalBrain.Invocation;
using DigitalBrain.Kernel;
using DigitalBrain.Rules;
using DigitalBrain.Runtime.Orleans.Access;
using DigitalBrain.Neurons;
using DigitalBrain.Units;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Orleans;
using Orleans.Runtime;
using Orleans.Storage;
using DigitalBrain.Runtime.Orleans.Storage;
using DigitalBrain.Runtime.Orleans.Synapses;

namespace DigitalBrain.Runtime.Orleans.Hosting;

/// <summary>
/// What the three host grains share: parse the key <c>brain/type/key</c>, resolve the plain class, build and attach it, then
/// dispatch each <see cref="RuntimeInvocation"/> through the one <see cref="NeuronBinder"/>. The invocation's Behavior is ignored:
/// provenance comes from RequestContext, stamped by <see cref="OrleansInvocationSink"/>.
/// </summary>
internal abstract partial class NeuronHostGrainBase(NeuronTypeRegistry types, IGrainFactory grains) : Grain, INeuronHost, Synapses.ISynapseHost, IRemindable, IHostTicks
{
    private readonly Dictionary<string, IGrainTimer> _timers = new(StringComparer.Ordinal);
    /// <summary>The binder every host grain dispatches through; its cache also serves scheduling policy checks.</summary>
    internal static readonly NeuronBinder Binder = new();

    private readonly Queue<Func<Task>> _deferred = new();
    private Neuron? _neuron;
    private Type? _class;
    private string _key = string.Empty;
    private NeuronId _id;
    private AccessGate _access = null!;
    private ICallerCarrier _callers = null!;
    private Delivery? _delivery;

    internal Neuron? ActiveNeuron => _neuron;

    /// <summary>The host grain type this class implements.</summary>
    protected abstract string GrainTypeName { get; }

    /// <inheritdoc />
    public override async Task OnActivateAsync(CancellationToken cancellationToken)
    {
        _key = this.GetPrimaryKeyString();
        if (!NeuronId.TryParse(_key, out var id)) { throw new BindingException($"'{_key}' is not a neuron id brain/type/key."); }
        _id = id;
        _access = ServiceProvider.GetService<AccessGate>() ?? new AccessGate(OrleansCallerAmbient.Instance, new AccessOptions(), new AccessCounters());
        _callers = ServiceProvider.GetService<ICallerCarrier>() ?? OrleansCallerAmbient.Instance;
        if (!types.TryGetRegistration(id.Type, out var registration)
            && !(await Restore(id, cancellationToken).ConfigureAwait(true) && types.TryGetRegistration(id.Type, out registration)))
        {
            throw new BindingException($"No neuron class is hosted for type '{id.Type}'.");
        }
        var neuronClass = registration.Class;
        var expected = NeuronTypeRegistry.GrainTypeOf(registration.Policy);
        if (expected != GrainTypeName) { throw new BindingException($"{neuronClass.FullName} is hosted by {expected}, not {GrainTypeName}."); }
        _class = neuronClass;
        var bound = new NeuronServices(ServiceProvider, id, ServiceProvider.GetServices<NeuronBinding>());
        var neuron = Construct(bound, neuronClass);
        var logs = ServiceProvider.GetService<BrainLogs>();
        BrainLog? log = logs is not null ? await logs.For(id.Brain.Value).ConfigureAwait(true) : null;
        var time = (TimeProvider?)ServiceProvider.GetService(typeof(TimeProvider)) ?? TimeProvider.System;
        var histories = logs is not null ? ServiceProvider.GetService<BrainSignalHistories>() : null;
        var history = (BrainSignalHistory?)histories?.For(id.Brain);
        IBrainJournal journal = logs is not null && history is not null
            ? new BrainLogJournal(logs, history, _ => this.GetGrainId().ToString())
            : UnregisteredJournal.Instance;
        IJournalView view = log ?? (IJournalView)journal;
        var consumer = new HostConsumer(this);
        var counters = ServiceProvider.GetService<DeliveryCounters>() ?? new DeliveryCounters();
        var delivery = new Delivery(
            id,
            journal,
            view,
            consumer,
            alias => types.TryGetRegistration(alias, out var r) ? r.Class : null,
            time,
            counters);
        await delivery.Initialize(cancellationToken).ConfigureAwait(true);

        var nudge = (ISignalNudge?)log?.Fanout ?? new Fanout(
            view,
            new GrainNudge(grains, types),
            alias => types.TryGetRegistration(alias, out var r) ? r.Class : null,
            (pub, sigs) =>
            {
                ServiceProvider.GetService<Signalling.ClusterListeners>()?.Offer(pub, sigs);
            });
        var context = new HostNeuronContext(id, this.GetGrainId(), registration, bound, grains, types,
            ServiceProvider.GetRequiredService<ILocalSiloDetails>().SiloAddress.ToString(), this, _deferred.Enqueue, delivery, journal, nudge);
        NeuronAttachment.Attach(neuron, context);
        await NeuronAttachment.Activate(neuron, cancellationToken).ConfigureAwait(true);
        _neuron = neuron;
        _delivery = delivery;
        RegisterExtensions(neuron, neuronClass);
        await delivery.CatchUp().ConfigureAwait(true);
    }

    private void RegisterExtensions(Neuron neuron, Type neuronClass)
    {
        foreach (var iface in neuronClass.GetInterfaces())
        {
            if (typeof(IGrainExtension).IsAssignableFrom(iface) && iface != typeof(IGrainExtension))
            {
                RegisterExtension(iface, neuron);
            }
        }
    }

    private void RegisterExtension(Type extensionType, object extension)
    {
        var nonGeneric = typeof(IGrainContext).GetMethod(nameof(IGrainContext.SetComponent), [typeof(Type), typeof(object)]);
        if (nonGeneric is not null)
        {
            nonGeneric.Invoke(GrainContext, [extensionType, extension]);
        }
        else
        {
            var generic = typeof(IGrainContext).GetMethods()
                .First(m => m.Name == nameof(IGrainContext.SetComponent) && m.IsGenericMethodDefinition);
            generic.MakeGenericMethod(extensionType).Invoke(GrainContext, [extension]);
        }
    }

    // ActivatorUtilities caches constructors per type in a process-wide map, which would pin the load context of a generated
    // (collectible) neuron class for good; a collectible class is built from its widest public constructor with services instead.
    // Generated neuron classes are UnitNeuron<TRecord>, whose single public constructor takes only services, so the choice is unambiguous.
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
        return Task.FromResult(CallPolicy.MayInterleave(neuron.GetType(), bound, call));
    }

    private (Neuron Neuron, BoundCall Call, Caller As) Admitted(RuntimeInvocation call)
    {
        var (neuron, bound) = Prepare(call);
        return (neuron, bound, _access.Admit(_id, bound));
    }

    /// <inheritdoc />
    public async Task<JsonElement?> Invoke(RuntimeInvocation call, CancellationToken cancellationToken = default)
    {
        var (neuron, bound, caller) = Admitted(call);
        using var running = _callers.Enter(caller);
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
        var (neuron, bound, caller) = Admitted(call);
        using var running = _callers.Enter(caller);
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
        var (neuron, bound, caller) = Admitted(call);
        using var running = _callers.Enter(caller);
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

    private void RequireInterleaving(RuntimeInvocation call)
    {
        var (neuron, bound) = Prepare(call);
        if (!CallPolicy.MayInterleave(neuron.GetType(), bound, call))
        { throw new BindingException($"{bound.Contract.FullName}.{bound.Method.Name} does not permit interleaving."); }
    }

    /// <inheritdoc />
    public async Task Admit(NeuronId subscriber, string[] signalAliases)
    {
        ArgumentNullException.ThrowIfNull(signalAliases);
        if (_neuron is not { } neuron) { throw new InvalidOperationException($"The neuron {_key} is not active."); }
        // Admission is decided under the subscriber's identity: a caller may not ask on behalf of another neuron (T-04-36).
        var caller = (RequestContext.Get(HostNeuronContext.CallerKey) as string) ?? _callers.Current?.Via?.ToString() ?? string.Empty;
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
    public async Task Nudge(long upTo)
    {
        if (_delivery is { } delivery)
        {
            await delivery.Nudged(new Position(upTo)).ConfigureAwait(true);
        }
    }

    /// <inheritdoc />
    public Task EnsureBrainWide(string[] signalAliases)
    {
        EnsureInstallsCaller();
        return _delivery?.EnsureBrainWide(signalAliases, (ServiceProvider.GetService<TimeProvider>() ?? TimeProvider.System).GetUtcNow(), false) ?? Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task RemoveBrainWide()
    {
        EnsureInstallsCaller();
        return _delivery?.RemoveBrainWide() ?? Task.CompletedTask;
    }

    // Reaction edges are wired at install time: only the installs neuron of this brain asks (OrleansInstallHooks).
    private void EnsureInstallsCaller()
    {
        if (_neuron is null) { throw new InvalidOperationException($"The neuron {_key} is not active."); }
        var installs = new NeuronId(NeuronId.Parse(_key).Brain, OrleansInstallHooks.InstallsType, "default").ToString();
        var caller = (RequestContext.Get(HostNeuronContext.CallerKey) as string) ?? _callers.Current?.Via?.ToString() ?? string.Empty;
        if (!string.Equals(caller, installs, StringComparison.Ordinal))
        {
            throw new UnauthorizedAccessException($"The brain-wide synapse of {_key} was asked by '{caller}', not by {installs}.");
        }
    }

    /// <inheritdoc />
    public Task CatchUp() => _delivery?.CatchUp() ?? Task.CompletedTask;

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
        using var running = _callers.Enter(Caller.OfKernel(_id.Brain));
        try { await NeuronAttachment.Remind(neuron, name, cancellationToken).ConfigureAwait(true); }
        finally { await Drain().ConfigureAwait(true); }
    }

    /// <summary>The contract <paramref name="contract"/> names on <paramref name="neuronClass"/>, or null when the class does not implement it.</summary>
    internal static Type? ContractOf(Type neuronClass, string contract)
        => CallPolicy.FindContract((neuronClass, contract));

    /// <summary>Binds <paramref name="call"/> against the contract it names on <paramref name="neuronClass"/>.</summary>
    /// <exception cref="BindingException">The class does not implement the contract, or the call does not bind.</exception>
    internal static BoundCall Bind(Type neuronClass, RuntimeInvocation call)
        => CallPolicy.Bind(Binder, neuronClass, call);

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
