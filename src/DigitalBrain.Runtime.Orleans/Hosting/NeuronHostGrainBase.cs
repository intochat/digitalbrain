using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
using System.Text.Json;
using DigitalBrain.Invocation;
using DigitalBrain.Spi;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Orleans;
using Orleans.Runtime;
using Orleans.Streams.Core;
using Orleans.Storage;
using DigitalBrain.Runtime.Orleans.Synapses;

namespace DigitalBrain.Runtime.Orleans.Hosting;

/// <summary>
/// What the three host grains share: parse the key <c>brain/type/key</c>, resolve the plain class, build and attach it, then
/// dispatch each <see cref="RuntimeInvocation"/> through the one <see cref="NeuronBinder"/>. The invocation's Behavior is ignored:
/// provenance comes from RequestContext, stamped by <see cref="OrleansInvocationSink"/>.
/// </summary>
internal abstract partial class NeuronHostGrainBase(NeuronTypeRegistry types, IGrainFactory grains) : Grain, INeuronHost, ISynapseHost, IStreamSubscriptionObserver, IRemindable, IHostTicks
{
    private readonly Dictionary<string, IGrainTimer> _timers = new(StringComparer.Ordinal);
    private readonly Dictionary<string, TimeSpan?> _keepAliveTimers = new(StringComparer.Ordinal);
    private static readonly TimeSpan KeepAliveMargin = TimeSpan.FromSeconds(5);
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
        if (!types.TryGetRegistration(id.Type, out var registration)) { throw new BindingException($"No neuron class is hosted for type '{id.Type}'."); }
        var neuronClass = registration.Class;
        var expected = NeuronTypeRegistry.GrainTypeOf(registration.Policy);
        if (expected != GrainTypeName) { throw new BindingException($"{neuronClass.FullName} is hosted by {expected}, not {GrainTypeName}."); }
        _class = neuronClass;
        var neuron = (Neuron)ActivatorUtilities.CreateInstance(ServiceProvider, neuronClass);
        var synapses = new SynapseEngine(id, this.GetGrainId(), registration, ServiceProvider, grains, types, Drain);
        var context = new HostNeuronContext(id, this.GetGrainId(), registration, ServiceProvider, grains, types,
            ServiceProvider.GetRequiredService<ILocalSiloDetails>().SiloAddress.ToString(), this, _deferred.Enqueue, synapses);
        NeuronAttachment.Attach(neuron, context);
        await NeuronAttachment.Activate(neuron, cancellationToken).ConfigureAwait(true);
        _neuron = neuron;
        _synapses = synapses;
        synapses.Neuron = neuron;
        // Catch-up runs on every activation, before callers or live references reach the neuron (04 D-12; spike: catch-up first,
        // live duplicates are then dropped by the cursor).
        await synapses.Load().ConfigureAwait(true);
        await synapses.CatchUp().ConfigureAwait(true);
        // A publisher announces its own last again, so subscribers that missed a reference catch up (04 D-01).
        if (registration.Policy != HostPolicy.Stateless) { await context.HostState.Reannounce().ConfigureAwait(true); }
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
    public Task CatchUp() => _synapses?.CatchUp() ?? Task.CompletedTask;

    /// <summary>Resumes the explicit subscription on this activation; never subscribes again (04 D-12).</summary>
    public Task OnSubscribed(IStreamSubscriptionHandleFactory handleFactory)
        => _synapses is { } synapses ? synapses.OnSubscribed(handleFactory) : Task.CompletedTask;

    /// <inheritdoc />
    public Task ReceiveReminder(string reminderName, TickStatus status) => Tick(reminderName, CancellationToken.None);

    void IHostTicks.StartTimer(string name, TimeSpan due, TimeSpan? period, bool keepAlive)
    {
        if (_timers.Remove(name, out var existing)) { existing.Dispose(); }
        _timers[name] = this.RegisterGrainTimer(static (state, ct) => state.Grain.TimerTick(state.Name, ct), (Grain: this, Name: name),
            new GrainTimerCreationOptions(due, period ?? Timeout.InfiniteTimeSpan) { Interleave = false, KeepAlive = keepAlive });
        if (keepAlive)
        {
            // Opt-in keep-alive (04-18): hold the activation past its idle window until the first tick; renewed per tick.
            _keepAliveTimers[name] = period;
            this.DelayDeactivation(due + KeepAliveMargin);
        }
        else { ReleaseKeepAlive(name); }
    }

    void IHostTicks.Deactivate() => this.DeactivateOnIdle();

    void IHostTicks.StopTimer(string name)
    {
        if (_timers.Remove(name, out var existing)) { existing.Dispose(); }
        ReleaseKeepAlive(name);
    }

    // A timer tick: renews a periodic keep-alive, or releases a one-shot one, before the turn runs.
    private Task TimerTick(string name, CancellationToken cancellationToken)
    {
        if (_keepAliveTimers.TryGetValue(name, out var period))
        {
            if (period is { } every && every > TimeSpan.Zero && every != Timeout.InfiniteTimeSpan) { this.DelayDeactivation(every + KeepAliveMargin); }
            else { ReleaseKeepAlive(name); }
        }
        return Tick(name, cancellationToken);
    }

    // Normal idle collection resumes once no keep-alive timer remains.
    private void ReleaseKeepAlive(string name)
    {
        if (_keepAliveTimers.Remove(name) && _keepAliveTimers.Count == 0) { this.DelayDeactivation(TimeSpan.Zero); }
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
