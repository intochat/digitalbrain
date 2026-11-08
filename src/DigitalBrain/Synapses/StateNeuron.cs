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
}

/// <summary>The neuron that runs the gate, reaction and view synapses of one <c>[State]</c> record.</summary>
/// <typeparam name="TState">The state record.</typeparam>
/// <remarks>
/// After an allowance the neuron holds the allowed next state as provisional, keyed by the gated signal (in arrival order), until the
/// host calls <see cref="Committed"/> or <see cref="Aborted"/>. A later gate call evaluates against the newest provisional state, so
/// two publishers gated back to back cannot both pass a check only one of them may pass; each entry's next state includes every
/// earlier entry. Commits are head-ordered: <see cref="Committed"/> marks its entry, and the neuron saves only once the oldest entry is
/// committed, saving the next state of the longest committed run from the head (once) and removing that run, so a commit arriving out
/// of order never rolls the state back. <see cref="Aborted"/> removes only its entry and re-runs the gate functions of every later
/// entry, in order, on the new base; a re-gate that refuses keeps the entry (its publisher appended or will) with the base unchanged as
/// its next state and publishes an ephemeral <see cref="Rejected"/> whose operation ends in <c> (late)</c>, so the late refusal is
/// visible and never counted. Provisional entries older than <see cref="StateNeuron.ProvisionalLease"/> are discarded on the next gate
/// call. Entries are matched by the <see cref="Verdict.Allowed.Ticket"/> <see cref="Gate"/> assigned, so value-equal signals of two
/// publishers never take each other's entry; the ticket is the host's attempt id for the gate call, and an empty one is refused.
/// <para>A view synapse returning a <c>DigitalBrain.UI.View</c> stores it, as wire JSON, as the alias's current view with the next
/// generation, saved with the state in the same save that journals <see cref="ViewChanged"/>, and serves it through
/// <see cref="IViewReader"/>; so the view and its generation survive deactivation and restart. Any other view result is not stored
/// and is logged as a warning once per synapse. The state record itself is read through <see cref="IStateReader"/>.</para>
/// </remarks>
public sealed class StateNeuron<TState> : Neuron<TState>, ISynapseReceiver, IViewReader, IStateReader where TState : class, new()
{
    private const string GatePlaceholder = "gate";
    private readonly ISynapseCatalogSource _catalog;
    private readonly ISlotResolver _slots;
    private const int FlushedMemory = 256;
    private readonly List<Entry> _provisional = [];
    private readonly HashSet<Guid> _flushed = [];
    private readonly Queue<Guid> _flushedOrder = new();
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

    // The alias of the record in the current catalog; null once it was removed.
    private static string? AliasIn(SynapseCatalog catalog) => catalog.States.FirstOrDefault(s => s.Value == typeof(TState)).Key;

    // The synapses of the record in the current catalog: a catalog change reaches an active neuron on its next turn.
    private IReadOnlyList<SynapseDescriptor> Synapses()
    {
        var current = _catalog.Current;
        return AliasIn(current) is { } alias ? current.For(alias) : [];
    }

    /// <inheritdoc />
    public Task Receive(Signal signal, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(signal);
        return React(signal, null, null, ct);
    }

    /// <inheritdoc />
    /// <remarks>An alias reaction gets the entry as a <see cref="SignalEnvelope"/>: the payload is the signal serialised with the journal's options.</remarks>
    public Task Receive(JournaledSignal journaled, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(journaled);
        return React(journaled.Signal, journaled.TraceId, journaled.CausationId, ct);
    }

    // Runs every reaction the signal reaches, typed or by alias, in registration order; an alias reaction's envelope is built inside
    // its run (once, then reused), so a payload that cannot be serialised fails that reaction like any other failure.
    private async Task React(Signal signal, string? traceId, string? causationId, CancellationToken ct)
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
                    await Save(r.Next, r.Signals.Count == 0 ? [Changed(signal)] : r.Signals).ConfigureAwait(true);
                }
                else
                {
                    await Save((TState)result!, [Changed(signal)]).ConfigureAwait(true);
                }

                return "saved";
            }, ct, build).ConfigureAwait(true);
            await PendingViews().ConfigureAwait(true);
        }
    }

    // An untyped signal (no loaded unit provides its type) carries the journal's raw JSON; a typed one is serialised with the journal's options.
    private static SignalEnvelope Envelope(Signal signal, string? traceId, string? causationId)
    {
        if (signal is IUntypedSignal untyped) { return new SignalEnvelope(untyped.Alias, untyped.Payload, traceId, causationId); }
        var type = signal.GetType();
        return new SignalEnvelope(AliasOf(signal), JsonSerializer.SerializeToElement(signal, type, RuntimeProtocol.OptionsFor(type)), traceId, causationId);
    }

    // The reactions of the signal: typed ones whose declared type it is (never for an untyped signal), alias ones whose alias is its alias.
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
        // Only uncommitted entries expire; a committed entry's append is durable, so it is flushed once nothing uncommitted is ahead of it.
        if (_provisional.RemoveAll(entry => !entry.Committed && now - entry.At > StateNeuron.ProvisionalLease) > 0)
        {
            await FlushHead(null).ConfigureAwait(true);
        }
        var current = _provisional.Count > 0 ? _provisional[^1].Next : State;
        var verdict = await Evaluate(signal, current, late: false, ct).ConfigureAwait(true);
        if (verdict is Verdict.Allowed { Next: TState provisional } allowance)
        {
            _provisional.Add(new Entry(signal, provisional, now, attempt));
            return allowance with { Ticket = attempt };
        }

        return verdict;
    }

    // Runs the gate chain for signal on current; a refusal is journaled, or when late published ephemerally as "<name> (late)".
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
            // Each gate of the chain sees what the gate before it allowed.
            if (verdict is Verdict.Allowed { Next: TState next }) { current = next; }
        }

        return verdict;
    }

    /// <summary>
    /// Marks the provisional entry of the ticket of <paramref name="allowed"/> committed; when the oldest entry is committed, saves the next state of
    /// the longest committed run from the head once and removes that run (a commit behind an uncommitted head saves nothing yet).
    /// Matches by ticket only; an empty ticket is refused. Committed entries never expire; an uncommitted head that outlives
    /// <see cref="StateNeuron.ProvisionalLease"/> is pruned on the next gate call, which then flushes the committed run behind it.
    /// A repeated commit of a ticket the neuron already flushed (it remembers the last 256) is a no-op, so a duplicate never re-saves
    /// an older state. A ticket that is neither provisional nor remembered as flushed, which is only reachable after the entry
    /// outlived the lease, saves the next state of <paramref name="allowed"/>.
    /// </summary>
    /// <param name="signal">The published signal.</param>
    /// <param name="allowed">The verdict <see cref="Gate"/> returned, carrying its ticket.</param>
    /// <returns>A task that completes when the state is saved or the entry marked.</returns>
    /// <exception cref="ArgumentException">The ticket of <paramref name="allowed"/> is <see cref="Guid.Empty"/>.</exception>
    public async Task Committed(Signal signal, Verdict.Allowed allowed)
    {
        ArgumentNullException.ThrowIfNull(signal);
        ArgumentNullException.ThrowIfNull(allowed);
        RequireTicket(allowed.Ticket, nameof(allowed));
        var index = Provisional(allowed.Ticket);
        if (index < 0)
        {
            if (_flushed.Contains(allowed.Ticket)) { return; }
            await Save((TState)allowed.Next, [Changed(signal)]).ConfigureAwait(true);
            await PendingViews().ConfigureAwait(true);
            return;
        }

        _provisional[index].Committed = true;
        await FlushHead(signal).ConfigureAwait(true);
    }

    // Saves the next state of the longest committed run from the head once, removes the run and runs the views; nothing when the head is uncommitted.
    private async Task FlushHead(Signal? cause)
    {
        var run = 0;
        while (run < _provisional.Count && _provisional[run].Committed) { run++; }
        if (run == 0) { return; }
        var next = _provisional[run - 1].Next;
        cause ??= _provisional[run - 1].Signal;
        for (var i = 0; i < run; i++) { Remember(_provisional[i].Ticket); }
        _provisional.RemoveRange(0, run);
        await Save(next, [Changed(cause)]).ConfigureAwait(true);
        await PendingViews().ConfigureAwait(true);
    }

    /// <summary>
    /// Removes the provisional entry of <paramref name="ticket"/> without saving, then re-runs the gate functions of every later entry,
    /// in order, on the new base; a re-gate that refuses keeps its entry with the base unchanged as its next state and publishes an
    /// ephemeral <see cref="Rejected"/> whose operation ends in <c> (late)</c>; a re-gate that throws is treated the same way, with the
    /// exception message, and never escapes. If the head run is then committed, it is saved once and the views run. An abort whose
    /// entry is committed, already flushed or was never added (the gate refused, threw or timed out before allowing) is ignored, so a host may abort a gate
    /// whose verdict never arrived with its attempt id. Matches by ticket only; an empty ticket is refused.
    /// </summary>
    /// <param name="signal">The signal whose publish was abandoned.</param>
    /// <param name="ticket">The attempt id of the gate call (the <see cref="Verdict.Allowed.Ticket"/>) whose entry to drop.</param>
    /// <returns>A task that completes when the later entries are re-gated and a committed head is saved.</returns>
    /// <exception cref="ArgumentException"><paramref name="ticket"/> is <see cref="Guid.Empty"/>.</exception>
    public async Task Aborted(Signal signal, Guid ticket)
    {
        ArgumentNullException.ThrowIfNull(signal);
        RequireTicket(ticket, nameof(ticket));
        var index = Provisional(ticket);
        if (index < 0) { return; }
        _provisional.RemoveAt(index);
        var current = index > 0 ? _provisional[index - 1].Next : State;
        for (var i = index; i < _provisional.Count; i++)
        {
            var entry = _provisional[i];
            try
            {
                var verdict = await Evaluate(entry.Signal, current, late: true, CancellationToken.None).ConfigureAwait(true);
                entry.Next = verdict is Verdict.Allowed { Next: TState next } ? next : current;
            }
#pragma warning disable CA1031 // A re-gate failure is a late refusal: the host is already handling a failed publish.
            catch (Exception error)
            {
                entry.Next = current;
                var name = (Matching(SynapseKind.Gate, entry.Signal).FirstOrDefault()?.Name ?? GatePlaceholder) + " (late)";
                try { await PublishEphemeral(new Rejected(name, [name], [error.Message])).ConfigureAwait(true); }
                catch (Exception) { /* the late refusal is advisory */ }
            }
#pragma warning restore CA1031

            current = entry.Next;
        }

        // The aborted entry may have been the uncommitted head of a committed run: save that run once now.
        await FlushHead(null).ConfigureAwait(true);
    }

    /// <summary>Marks the views to run over the new state once the saving synapse has reported its outcome; a journaled refusal leaves the state as it was and runs none.</summary>
    protected override void OnCommitted()
    {
        _viewsPending |= !_journalingRefusal && !_savingView;
    }

    private static string AliasOf(Signal signal) => SignalAliases.Of(signal);

    // Remembers a flushed ticket, forgetting the oldest beyond the cap.
    private void Remember(Guid ticket)
    {
        if (!_flushed.Add(ticket)) { return; }
        _flushedOrder.Enqueue(ticket);
        if (_flushedOrder.Count > FlushedMemory) { _flushed.Remove(_flushedOrder.Dequeue()); }
    }

    // The uncommitted provisional entry of the ticket.
    private int Provisional(Guid ticket) => _provisional.FindIndex(entry => !entry.Committed && entry.Ticket == ticket);

    private static void RequireTicket(Guid ticket, string name)
    {
        if (ticket == Guid.Empty) { throw new ArgumentException("A gate entry is matched by a non-empty ticket (the host's attempt id).", name); }
    }

    private StateChanged Changed(Signal cause) => new(Id.ToString(), AliasOf(cause));

    // A provisional allowance: its next state is rewritten when an earlier entry aborts.
    private sealed class Entry(Signal signal, TState next, DateTimeOffset at, Guid ticket)
    {
        public Guid Ticket { get; } = ticket;

        public Signal Signal { get; } = signal;

        public TState Next { get; set; } = next;

        public DateTimeOffset At { get; } = at;

        public bool Committed { get; set; }
    }

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
                    var previous = _view;
                    _view = new PublishedView(alias, (previous?.Generation ?? 0) + 1, JsonSerializer.SerializeToElement(result, type, RuntimeProtocol.OptionsFor(type)));
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
    public async Task Activate(string alias, long generation, string nodeKey, string? fieldValue)
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

        // Journaled from the state neuron with the caller's cause; a state neuron never hears its own signals, so it reacts here.
        var signal = new EmittedSignal(emit.Signal, ViewNodes.Payload(emit.Args, fieldValue));
        // The neuron is the only writer of its own journal and this is its turn: its last sequence after the publish is the emit's.
        // (A runtime may leave a signal of no loaded type out of a read's page, but never out of the page's Last.)
        // The trace is taken before the publish, in this flow, so the publish's stamp and the envelope agree on a freshly minted one.
        var trace = Context.TraceId;
        await Publish(signal).ConfigureAwait(true);
        var sequence = (await Context.History.Read(Id, long.MaxValue, 1, CancellationToken.None).ConfigureAwait(true)).Last;
        await React(signal, trace, Causation.Address(Id, sequence), CancellationToken.None).ConfigureAwait(true);
    }

    // The state is stored with the current view beside it; a state stored before views were kept loads as the bare record.
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

    private async Task Run(SynapseDescriptor synapse, Signal? signal, TState state, Func<object?, Task<string>> settle, CancellationToken ct, Func<SignalEnvelope>? envelope = null)
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
        catch
        {
            await PublishEphemeral(new SynapseFinished(neuron, synapse.Name, "failed", clock.Elapsed.TotalMilliseconds)).ConfigureAwait(true);
            throw;
        }

        await PublishEphemeral(new SynapseFinished(neuron, synapse.Name, outcome, clock.Elapsed.TotalMilliseconds)).ConfigureAwait(true);
    }

    // Slots are resolved sequentially in parameter order, once per call. An unbound slot refuses a gate by name; any other synapse fails.
    private async Task<object?> Invoke(SynapseDescriptor synapse, Signal? signal, SignalEnvelope? envelope, TState state, CancellationToken ct)
    {
        var parameters = synapse.Function.GetParameters();
        var arguments = new object?[parameters.Length];
        for (var i = 0; i < parameters.Length; i++)
        {
            var type = parameters[i].ParameterType;
            arguments[i] = type == typeof(CancellationToken) ? ct
                : type == typeof(SignalEnvelope) ? envelope
                : typeof(Signal).IsAssignableFrom(type) ? signal
                : state;
        }

        foreach (var slot in synapse.Slots.OrderBy(slot => slot.ParameterIndex))
        {
            try { arguments[slot.ParameterIndex] = await _slots.Resolve(Id, slot, ct).ConfigureAwait(true); }
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
            await task.ConfigureAwait(true);
            return task.GetType().GetProperty("Result")!.GetValue(task);
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
}
