using System.Diagnostics;
using System.Reflection;
using System.Runtime.ExceptionServices;
using System.Text.Json;
using DigitalBrain.Invocation;
using DigitalBrain.Rules;
using Verdict = DigitalBrain.Rules.Check;
using DigitalBrain.Spi;

namespace DigitalBrain;

/// <summary>The lease of a state neuron's provisional states.</summary>
public static class StateNeuron
{
    /// <summary>
    /// How long a provisional next state (an allowance not yet committed or aborted) stays in force; an older one is discarded on
    /// the next gate call, so a publisher that died between its gate and its append does not hold the gate forever.
    /// </summary>
    public static readonly TimeSpan ProvisionalLease = TimeSpan.FromSeconds(30);

    /// <summary>The maximum wall-clock duration a pure synapse (gate, reaction, or view with no bound capability slots) may run on one turn.</summary>
    public static readonly TimeSpan ReactionTimeout = TimeSpan.FromSeconds(5);

    /// <summary>The maximum wall-clock duration a synapse with bound capability slots may run on one turn.</summary>
    public static readonly TimeSpan SlotReactionTimeout = TimeSpan.FromSeconds(30);

    /// <summary>The maximum serialized UTF-8 byte size (<c>2 MiB</c>) of a state record saved by a state neuron.</summary>
    public const int MaxStateBytes = 2 * 1024 * 1024;
}

/// <summary>The neuron that runs the gate, reaction and view synapses of one <c>[State]</c> record.</summary>
/// <typeparam name="TState">The state record.</typeparam>
public sealed class StateNeuron<TState> : Neuron<TState>, ISynapseReceiver, IViewReader, IStateReader where TState : class, new()
{
    private const string GatePlaceholder = "gate";
    private readonly ISynapseCatalogSource _catalog;
    private readonly ISlotResolver _slots;
    private readonly ProvisionalGateQueue<TState> _gateQueue = new();
    private bool _viewsPending;
    private bool _journalingRefusal;
    private PublishedView? _view;
    private bool _savingView;
    private bool _stored;
    private static readonly System.Collections.Concurrent.ConcurrentDictionary<string, byte> WarnedViews = new(StringComparer.Ordinal);

    /// <summary>Creates the neuron for <typeparamref name="TState"/> over the live <paramref name="catalog"/>, read again on every turn.</summary>
    /// <param name="catalog">The live synapse catalog.</param>
    /// <param name="slots">Resolves the <c>[Slot]</c> parameters of the synapse functions, once per call.</param>
    /// <exception cref="ArgumentException">The current catalog has no <c>[State]</c> alias for <typeparamref name="TState"/>.</exception>
    public StateNeuron(ISynapseCatalogSource catalog, ISlotResolver slots)
    {
        ArgumentNullException.ThrowIfNull(catalog);
        ArgumentNullException.ThrowIfNull(slots);
        _slots = slots;
        _catalog = catalog;
        _ = AliasIn(catalog.Current)
            ?? throw new ArgumentException($"{typeof(TState).FullName} is not a [State] record of the catalog.", nameof(catalog));
    }

    private static string? AliasIn(SynapseCatalog catalog) => catalog.States.FirstOrDefault(s => s.Value == typeof(TState)).Key;

    private IReadOnlyList<SynapseDescriptor> Synapses()
    {
        var current = _catalog.Current;
        return AliasIn(current) is { } alias ? current.For(alias) : [];
    }

    /// <inheritdoc />
    public Task Receive(Signal signal, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(signal);
        return React(signal, null, null, null, ct);
    }

    /// <inheritdoc />
    /// <remarks>An alias reaction gets the entry as a <see cref="SignalEnvelope"/>: the payload is the signal serialised with the journal's options.</remarks>
    public Task Receive(JournaledSignal journaled, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(journaled);
        return React(journaled.Signal, journaled.TraceId, journaled.CausationId, Causation.Address(journaled.Publisher, journaled.Sequence), ct);
    }

    private async Task React(Signal signal, string? traceId, string? causationId, string? causeId, CancellationToken ct)
    {
        SignalEnvelope? envelope = null;
        foreach (var reaction in Reactions(signal))
        {
            Func<SignalEnvelope>? build = reaction.SignalAlias is null ? null : () => envelope ??= Envelope(signal, traceId ?? Context.TraceId, causationId);
            await Run(reaction, signal, State, async result =>
            {
                if (ReferenceEquals(result, State)) { return "unchanged"; }
                if (result is Reaction<TState> r)
                {
                    IReadOnlyList<Signal> signals = r.Signals.Count == 0 ? [Changed(signal)] : r.Signals;
                    if (r.Asks.Count > 0) { signals = [.. signals, new Asked(Id.Type, Guid.NewGuid().ToString("N"), AskRules.Checked(r.Asks, Synapses()))]; }
                    await Save(r.Next, signals).ConfigureAwait(true);
                }
                else
                {
                    await Save((TState)result!, [Changed(signal)]).ConfigureAwait(true);
                }

                return "saved";
            }, ct, build, causeId).ConfigureAwait(true);
            await PendingViews().ConfigureAwait(true);
        }
    }

    private static SignalEnvelope Envelope(Signal signal, string? traceId, string? causationId)
    {
        if (signal is IUntypedSignal untyped) { return new SignalEnvelope(untyped.Alias, untyped.Payload, traceId, causationId); }
        var type = signal.GetType();
        return new SignalEnvelope(AliasOf(signal), JsonSerializer.SerializeToElement(signal, type, RuntimeProtocol.OptionsFor(type)), traceId, causationId);
    }

    private IEnumerable<SynapseDescriptor> Reactions(Signal signal)
    {
        var alias = AliasOf(signal);
        var untyped = signal is IUntypedSignal;
        return Synapses().Where(d => d.Kind == SynapseKind.Reaction
            && (d.SignalAlias is null ? !untyped && d.Signal!.IsInstanceOfType(signal) : string.Equals(d.SignalAlias, alias, StringComparison.Ordinal)));
    }

    /// <inheritdoc />
    public async Task<Verdict> Gate(Signal signal, Guid attempt, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(signal);
        RequireTicket(attempt, nameof(attempt));
        var now = Context.Time.GetUtcNow();
        if (_gateQueue.PruneExpiredUncommitted(now, StateNeuron.ProvisionalLease))
        {
            await FlushHead(null).ConfigureAwait(true);
        }

        var current = _gateQueue.EffectiveBase(State);
        var verdict = await Evaluate(signal, current, late: false, ct).ConfigureAwait(true);
        if (verdict is Verdict.Allowed { Next: TState provisional } allowance)
        {
            _gateQueue.RecordAllowance(signal, provisional, now, attempt);
            return allowance with { Ticket = attempt };
        }

        return verdict;
    }

    private async Task<Verdict> Evaluate(Signal signal, TState current, bool late, CancellationToken ct)
    {
        var verdict = Verdict.Allow(current);
        foreach (var gate in Matching(SynapseKind.Gate, signal))
        {
            await Run(gate, signal, current, async result =>
            {
                verdict = (Verdict)result!;
                if (verdict is not Verdict.Refused refused) { return "allowed"; }
                var names = refused.Broken.Select(rule => rule.Name == GatePlaceholder ? gate.Name : rule.Name).ToArray();
                var messages = refused.Broken.Select(rule => rule.Message).ToArray();
                if (late)
                {
                    await PublishEphemeral(new Rejected(gate.Name + " (late)", names, messages)).ConfigureAwait(true);
                    return "rejected";
                }

                _journalingRefusal = true;
                try { await Save(State, [new Rejected(gate.Name, names, messages)]).ConfigureAwait(true); }
                finally { _journalingRefusal = false; }
                return "rejected";
            }, ct).ConfigureAwait(true);
            if (verdict is Verdict.Refused) { return verdict; }
            if (verdict is Verdict.Allowed { Next: TState next }) { current = next; }
        }

        return verdict;
    }

    /// <summary>Marks the provisional entry of the ticket of <paramref name="allowed"/> committed and flushes any committed head run.</summary>
    public async Task Committed(Signal signal, Verdict.Allowed allowed)
    {
        ArgumentNullException.ThrowIfNull(signal);
        ArgumentNullException.ThrowIfNull(allowed);
        RequireTicket(allowed.Ticket, nameof(allowed));
        if (!_gateQueue.TryMarkCommitted(allowed.Ticket))
        {
            if (_gateQueue.WasFlushed(allowed.Ticket)) { return; }
            await Save((TState)allowed.Next, [Changed(signal)]).ConfigureAwait(true);
            await PendingViews().ConfigureAwait(true);
            return;
        }

        await FlushHead(signal).ConfigureAwait(true);
    }

    private async Task FlushHead(Signal? cause)
    {
        if (_gateQueue.DequeueCommittedHeadRun(cause) is not { } batch) { return; }
        await Save(batch.Next, [Changed(batch.Cause)]).ConfigureAwait(true);
        await PendingViews().ConfigureAwait(true);
    }

    /// <summary>Removes the provisional entry of <paramref name="ticket"/> without saving and re-evaluates later provisional entries on the updated base.</summary>
    public async Task Aborted(Signal signal, Guid ticket)
    {
        ArgumentNullException.ThrowIfNull(signal);
        RequireTicket(ticket, nameof(ticket));
        if (!_gateQueue.TryRemoveUncommitted(ticket, State, out var current, out var trailingEntries))
        {
            return;
        }

        foreach (var entry in trailingEntries)
        {
            try
            {
                var verdict = await Evaluate(entry.Signal, current, late: true, CancellationToken.None).ConfigureAwait(true);
                entry.Next = verdict is Verdict.Allowed { Next: TState next } ? next : current;
            }
#pragma warning disable CA1031
            catch (Exception error)
            {
                entry.Next = current;
                var name = (Matching(SynapseKind.Gate, entry.Signal).FirstOrDefault()?.Name ?? GatePlaceholder) + " (late)";
                try { await PublishEphemeral(new Rejected(name, [name], [error.Message])).ConfigureAwait(true); }
                catch (Exception) { }
            }
#pragma warning restore CA1031

            current = entry.Next;
        }

        await FlushHead(null).ConfigureAwait(true);
    }

    /// <summary>Marks the views to run over the new state once the saving synapse has reported its outcome; a journaled refusal leaves the state as it was and runs none.</summary>
    protected override void OnCommitted()
    {
        _viewsPending |= !_journalingRefusal && !_savingView;
    }

    private static string AliasOf(Signal signal) => SignalAliases.Of(signal);

    private static void RequireTicket(Guid ticket, string name)
    {
        if (ticket == Guid.Empty) { throw new ArgumentException("A gate entry is matched by a non-empty ticket (the host's attempt id).", name); }
    }

    private StateChanged Changed(Signal cause) => new(Id.ToString(), AliasOf(cause));

    private Task PendingViews()
    {
        if (!_viewsPending) { return Task.CompletedTask; }
        _viewsPending = false;
        return RunViews();
    }

    private async Task RunViews()
    {
        foreach (var view in Synapses().Where(d => d.Kind == SynapseKind.View))
        {
            await Run(view, null, State, async result =>
            {
                if (result is not null && result.GetType().FullName == ViewNodes.ViewType && AliasIn(_catalog.Current) is { } alias)
                {
                    var type = result.GetType();
                    var wire = JsonSerializer.SerializeToElement(result, type, RuntimeProtocol.OptionsFor(type));
                    if (ViewNodes.DeclaresBy(wire))
                    {
                        StateNeuronLog.ViewDeclaresBy(Logger(), view.Name);
                        return "refused";
                    }

                    var previous = _view;
                    _view = new PublishedView(alias, (previous?.Generation ?? 0) + 1, wire);
                    _savingView = true;
                    try { await Save(State, [new ViewChanged(alias, _view.Generation)]).ConfigureAwait(true); }
                    catch
                    {
                        _view = previous;
                        throw;
                    }
                    finally { _savingView = false; }
                }
                else if (WarnedViews.TryAdd(Id.Brain + "/" + view.Name, 0))
                {
                    StateNeuronLog.ViewNotStored(Logger(), view.Name, result?.GetType().FullName ?? "null");
                }

                return "viewed";
            }, CancellationToken.None).ConfigureAwait(true);
        }
    }

    /// <inheritdoc />
    public Task<PublishedView?> Read(string alias)
        => Task.FromResult(_view is { } view && string.Equals(view.Alias, alias, StringComparison.Ordinal) ? view : null);

    /// <inheritdoc />
    Task<JsonElement> IStateReader.Read(string alias)
        => Task.FromResult(_stored && string.Equals(AliasIn(_catalog.Current), alias, StringComparison.Ordinal)
            ? JsonSerializer.SerializeToElement(State, RuntimeProtocol.OptionsFor(typeof(TState)))
            : JsonSerializer.SerializeToElement<object?>(null));

    /// <inheritdoc />
    public async Task Activate(string alias, long generation, string nodeKey, string? fieldValue, string? by)
    {
        ArgumentNullException.ThrowIfNull(alias);
        ArgumentNullException.ThrowIfNull(nodeKey);
        if (_view is not { } view || !string.Equals(view.Alias, alias, StringComparison.Ordinal) || view.Generation != generation)
        {
            throw new ActivationRefusedException(ActivationRefusedException.Stale);
        }

        var node = ViewNodes.Find(view.View, nodeKey) ?? throw new ActivationRefusedException(ActivationRefusedException.UnknownNode);
        var emit = ViewNodes.EmitOf(node) ?? throw new ActivationRefusedException(ActivationRefusedException.NoEmit);
        if (!string.Equals(emit.Owner, alias, StringComparison.Ordinal)) { throw new ActivationRefusedException(ActivationRefusedException.ForeignOwner); }

        var signal = new EmittedSignal(emit.Signal, ViewNodes.Payload(emit.Args, fieldValue, by));
        var trace = Context.TraceId;
        await Publish(signal).ConfigureAwait(true);
        var sequence = (await Context.History.Read(Id, long.MaxValue, 1, CancellationToken.None).ConfigureAwait(true)).Last;
        var address = Causation.Address(Id, sequence);
        await React(signal, trace, address, address, CancellationToken.None).ConfigureAwait(true);
    }

    internal override async Task<TState?> LoadState()
    {
        var stored = await Context.State.Load<StoredState<TState>>().ConfigureAwait(true);
        if (stored?.State is { } state)
        {
            _stored = true;
            _view = stored.View;
            return state;
        }

        var bare = await Context.State.Load<TState>().ConfigureAwait(true);
        _stored = bare is not null;
        return bare;
    }

    internal override async Task StoreState(TState next, IReadOnlyList<Signal> signals)
    {
        var bytes = JsonSerializer.SerializeToUtf8Bytes(next, RuntimeProtocol.OptionsFor(typeof(TState)));
        if (bytes.Length > StateNeuron.MaxStateBytes)
        {
            throw new InvalidOperationException(
                $"State '{typeof(TState).Name}' of neuron '{Id}' serialized to {bytes.Length} bytes, exceeding the {StateNeuron.MaxStateBytes}-byte cap.");
        }

        await Context.State.Save(new StoredState<TState>(next, _view), signals).ConfigureAwait(true);
        _stored = true;
    }

    private Microsoft.Extensions.Logging.ILogger Logger()
    {
        try { return (Context.Services.GetService(typeof(Microsoft.Extensions.Logging.ILoggerFactory)) as Microsoft.Extensions.Logging.ILoggerFactory)?.CreateLogger("DigitalBrain.Synapses") ?? Microsoft.Extensions.Logging.Abstractions.NullLogger.Instance; }
        catch (InvalidOperationException) { return Microsoft.Extensions.Logging.Abstractions.NullLogger.Instance; }
    }

    private IEnumerable<SynapseDescriptor> Matching(SynapseKind kind, Signal signal) =>
        Synapses().Where(d => d.Kind == kind && d.SignalAlias is null && signal is not IUntypedSignal && d.Signal!.IsInstanceOfType(signal));

    private async Task Run(SynapseDescriptor synapse, Signal? signal, TState state, Func<object?, Task<string>> settle, CancellationToken ct, Func<SignalEnvelope>? envelope = null, string? causeId = null)
    {
        var neuron = Id.ToString();
        var arriving = signal is null ? AliasIn(_catalog.Current) ?? Id.Type : AliasOf(signal);
        await PublishEphemeral(new SynapseStarted(neuron, synapse.Name, arriving)).ConfigureAwait(true);
        var clock = Stopwatch.StartNew();
        string outcome;
        try
        {
            outcome = await settle(await Invoke(synapse, signal, envelope?.Invoke(), state, ct).ConfigureAwait(true)).ConfigureAwait(true);
        }
        catch (Exception error)
        {
            await PublishEphemeral(new SynapseFinished(neuron, synapse.Name, "failed", clock.Elapsed.TotalMilliseconds)).ConfigureAwait(true);
            if (synapse.Kind is SynapseKind.Reaction or SynapseKind.View && !string.Equals(arriving, "synapse.failed", StringComparison.Ordinal))
            {
                var message = error.Message.Length > 500 ? error.Message[..500] : error.Message;
#pragma warning disable CA1031
                try { await Publish(new SynapseFailed(AliasIn(_catalog.Current) ?? Id.Type, synapse.Name, arriving, causeId, error.GetType().Name, message)).ConfigureAwait(true); }
                catch (Exception) { }
#pragma warning restore CA1031
            }

            throw;
        }

        await PublishEphemeral(new SynapseFinished(neuron, synapse.Name, outcome, clock.Elapsed.TotalMilliseconds)).ConfigureAwait(true);
    }

    private async Task<object?> Invoke(SynapseDescriptor synapse, Signal? signal, SignalEnvelope? envelope, TState state, CancellationToken ct)
    {
        var budget = synapse.Slots.Any(slot => !slot.Deferred) ? StateNeuron.SlotReactionTimeout : StateNeuron.ReactionTimeout;
        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeoutCts.CancelAfter(budget);
        var linkedCt = timeoutCts.Token;
        var parameters = synapse.Function.GetParameters();
        var arguments = new object?[parameters.Length];
        for (var i = 0; i < parameters.Length; i++)
        {
            var type = parameters[i].ParameterType;
            arguments[i] = type == typeof(CancellationToken) ? linkedCt
                : type == typeof(SignalEnvelope) ? envelope
                : typeof(Signal).IsAssignableFrom(type) ? signal
                : state;
        }

        foreach (var slot in synapse.Slots.OrderBy(slot => slot.ParameterIndex))
        {
            if (slot.Deferred)
            {
                arguments[slot.ParameterIndex] = Activator.CreateInstance(typeof(Asks<>).MakeGenericType(slot.Contract), slot.Name);
                continue;
            }

            try { arguments[slot.ParameterIndex] = await _slots.Resolve(Id, slot, linkedCt).ConfigureAwait(true); }
            catch (SlotUnboundException) when (synapse.Kind == SynapseKind.Gate)
            {
                return Verdict.Reject($"slot '{slot.Name}' is not bound");
            }
        }

        object? returned;
        try { returned = synapse.Function.Invoke(null, arguments); }
        catch (TargetInvocationException error) when (error.InnerException is not null)
        {
            ExceptionDispatchInfo.Capture(error.InnerException).Throw();
            throw;
        }

        if (returned is Task task)
        {
            await task.WaitAsync(linkedCt).ConfigureAwait(true);
            var returnType = synapse.Function.ReturnType;
            var resultType = returnType.IsGenericType ? returnType.GetGenericArguments()[0] : null;
            return await TaskBoxing.ResultAsync(task, resultType).ConfigureAwait(true);
        }

        return returned;
    }
}

/// <summary>What a state neuron stores: its state record and its current view.</summary>
internal sealed record StoredState<TState>(TState? State, PublishedView? View) where TState : class;

internal static partial class StateNeuronLog
{
    [Microsoft.Extensions.Logging.LoggerMessage(EventId = 4140, EventName = "ViewNotStored", Level = Microsoft.Extensions.Logging.LogLevel.Warning,
        Message = "View synapse {Synapse} returned {Type}, not a DigitalBrain.UI.View; its result is not stored or announced.")]
    public static partial void ViewNotStored(Microsoft.Extensions.Logging.ILogger logger, string synapse, string type);

    [Microsoft.Extensions.Logging.LoggerMessage(EventId = 4141, EventName = "ViewDeclaresBy", Level = Microsoft.Extensions.Logging.LogLevel.Warning,
        Message = "View synapse {Synapse} declares the host-reserved \"by\" in an emit's args; the view is refused and not published.")]
    public static partial void ViewDeclaresBy(Microsoft.Extensions.Logging.ILogger logger, string synapse);
}
