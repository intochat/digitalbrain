using System.Diagnostics;
using System.Reflection;
using System.Runtime.ExceptionServices;
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
/// publishers never take each other's entry; a ticket of <see cref="Guid.Empty"/> (legacy) falls back to matching by signal value.
/// </remarks>
public sealed class StateNeuron<TState> : Neuron<TState>, ISynapseReceiver where TState : class, new()
{
    private const string GatePlaceholder = "gate";
    private readonly string _alias;
    private readonly IReadOnlyList<SynapseDescriptor> _synapses;
    private readonly List<Entry> _provisional = [];
    private bool _viewsPending;
    private bool _journalingRefusal;

    /// <summary>Creates the neuron for <typeparamref name="TState"/> from <paramref name="catalog"/>.</summary>
    /// <param name="catalog">The synapse catalog.</param>
    /// <exception cref="ArgumentException">The catalog has no <c>[State]</c> alias for <typeparamref name="TState"/>.</exception>
    public StateNeuron(SynapseCatalog catalog)
    {
        ArgumentNullException.ThrowIfNull(catalog);
        _alias = catalog.States.FirstOrDefault(s => s.Value == typeof(TState)).Key
            ?? throw new ArgumentException($"{typeof(TState).FullName} is not a [State] record of the catalog.", nameof(catalog));
        _synapses = catalog.For(_alias);
    }

    /// <inheritdoc />
    public async Task Receive(Signal signal, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(signal);
        foreach (var reaction in Matching(SynapseKind.Reaction, signal))
        {
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
            }, ct).ConfigureAwait(true);
            await PendingViews().ConfigureAwait(true);
        }
    }

    /// <inheritdoc />
    public async Task<Verdict> Gate(Signal signal, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(signal);
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
            var ticket = Guid.NewGuid();
            _provisional.Add(new Entry(signal, provisional, now, ticket));
            return allowance with { Ticket = ticket };
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
    /// Without a ticket (<see cref="Guid.Empty"/>, legacy) matches the oldest uncommitted entry of an equal signal. Committed entries never expire; an uncommitted head that outlives
    /// <see cref="StateNeuron.ProvisionalLease"/> is pruned on the next gate call, which then flushes the committed run behind it.
    /// Without an uncommitted provisional entry, which is only reachable after the entry outlived the lease, saves the next state of
    /// <paramref name="allowed"/>.
    /// </summary>
    /// <param name="signal">The published signal.</param>
    /// <param name="allowed">The verdict <see cref="Gate"/> returned, carrying its ticket.</param>
    /// <returns>A task that completes when the state is saved or the entry marked.</returns>
    public async Task Committed(Signal signal, Verdict.Allowed allowed)
    {
        ArgumentNullException.ThrowIfNull(signal);
        ArgumentNullException.ThrowIfNull(allowed);
        var index = Provisional(signal, allowed.Ticket);
        if (index < 0)
        {
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
        _provisional.RemoveRange(0, run);
        await Save(next, [Changed(cause)]).ConfigureAwait(true);
        await PendingViews().ConfigureAwait(true);
    }

    /// <summary>
    /// Removes the provisional entry of <paramref name="ticket"/> without saving, then re-runs the gate functions of every later entry,
    /// in order, on the new base; a re-gate that refuses keeps its entry with the base unchanged as its next state and publishes an
    /// ephemeral <see cref="Rejected"/> whose operation ends in <c> (late)</c>; a re-gate that throws is treated the same way, with the
    /// exception message, and never escapes. An abort whose entry is committed or gone is ignored. Without a ticket
    /// (<see cref="Guid.Empty"/>) matches the newest uncommitted entry of an equal signal: the one legitimate use of the value fallback,
    /// for a host aborting a gate that timed out before its verdict (and ticket) arrived, whose late entry is the newest one.
    /// Durable state is not touched.
    /// </summary>
    /// <param name="signal">The signal whose publish was abandoned.</param>
    /// <param name="ticket">The <see cref="Verdict.Allowed.Ticket"/> of the entry to drop.</param>
    /// <returns>A task that completes when the later entries are re-gated.</returns>
    public async Task Aborted(Signal signal, Guid ticket)
    {
        ArgumentNullException.ThrowIfNull(signal);
        var index = ticket != Guid.Empty
            ? Provisional(signal, ticket)
            : _provisional.FindLastIndex(entry => !entry.Committed && entry.Signal.Equals(signal));
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
    }

    /// <summary>Marks the views to run over the new state once the saving synapse has reported its outcome; a journaled refusal leaves the state as it was and runs none.</summary>
    protected override void OnCommitted()
    {
        _viewsPending |= !_journalingRefusal;
    }

    private static string AliasOf(Signal signal) => signal.GetType().GetCustomAttribute<SignalAttribute>()?.Alias ?? signal.GetType().Name;

    // The uncommitted provisional entry of the ticket; without a ticket (legacy), the oldest uncommitted entry for an equal signal
    // (records compare by value, so a host's decoded copy finds its entry).
    private int Provisional(Signal signal, Guid ticket) => ticket != Guid.Empty
        ? _provisional.FindIndex(entry => !entry.Committed && entry.Ticket == ticket)
        : _provisional.FindIndex(entry => !entry.Committed && entry.Signal.Equals(signal));

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
        foreach (var view in _synapses.Where(d => d.Kind == SynapseKind.View))
        {
            await Run(view, null, State, async result =>
            {
                await PublishEphemeral(new ViewChanged(Id.ToString(), result!)).ConfigureAwait(true);
                return "viewed";
            }, CancellationToken.None).ConfigureAwait(true);
        }
    }

    private IEnumerable<SynapseDescriptor> Matching(SynapseKind kind, Signal signal) =>
        _synapses.Where(d => d.Kind == kind && d.Signal!.IsInstanceOfType(signal));

    private async Task Run(SynapseDescriptor synapse, Signal? signal, TState state, Func<object?, Task<string>> settle, CancellationToken ct)
    {
        var neuron = Id.ToString();
        var arriving = signal is null ? _alias : AliasOf(signal);
        await PublishEphemeral(new SynapseStarted(neuron, synapse.Name, arriving)).ConfigureAwait(true);
        var clock = Stopwatch.StartNew();
        string outcome;
        try
        {
            outcome = await settle(await Invoke(synapse, signal, state, ct).ConfigureAwait(true)).ConfigureAwait(true);
        }
        catch
        {
            await PublishEphemeral(new SynapseFinished(neuron, synapse.Name, "failed", clock.Elapsed.TotalMilliseconds)).ConfigureAwait(true);
            throw;
        }

        await PublishEphemeral(new SynapseFinished(neuron, synapse.Name, outcome, clock.Elapsed.TotalMilliseconds)).ConfigureAwait(true);
    }

    private static async Task<object?> Invoke(SynapseDescriptor synapse, Signal? signal, TState state, CancellationToken ct)
    {
        var arguments = synapse.Function.GetParameters()
            .Select(p => p.ParameterType == typeof(CancellationToken) ? ct : typeof(Signal).IsAssignableFrom(p.ParameterType) ? signal : (object)state)
            .ToArray();
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
