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
/// host calls <see cref="Committed"/> (saved) or <see cref="Aborted"/> (dropped). A later gate call evaluates against the newest
/// provisional state, so two publishers gated back to back cannot both pass a check only one of them may pass. Aborting an entry also
/// drops every provisional entry gated after it: those were computed on an aborted base; their commits then save their verdict's own
/// next state, as a gate without a provisional entry does, and the hosts re-gate those publishers' next attempts. Provisional entries
/// older than <see cref="StateNeuron.ProvisionalLease"/> are discarded on the next gate call. Entries match by signal equality; equal
/// signals match the oldest entry first.
/// </remarks>
public sealed class StateNeuron<TState> : Neuron<TState>, ISynapseReceiver where TState : class, new()
{
    private const string GatePlaceholder = "gate";
    private readonly string _alias;
    private readonly IReadOnlyList<SynapseDescriptor> _synapses;
    private readonly List<(Signal Signal, TState Next, DateTimeOffset At)> _provisional = [];
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
        _provisional.RemoveAll(entry => now - entry.At > StateNeuron.ProvisionalLease);
        var current = _provisional.Count > 0 ? _provisional[^1].Next : State;
        var verdict = Verdict.Allow(current);
        foreach (var gate in Matching(SynapseKind.Gate, signal))
        {
            await Run(gate, signal, current, async result =>
            {
                verdict = (Verdict)result!;
                if (verdict is not Verdict.Refused refused) { return "allowed"; }
                var names = refused.Broken.Select(rule => rule.Name == GatePlaceholder ? gate.Name : rule.Name).ToArray();
                var messages = refused.Broken.Select(rule => rule.Message).ToArray();
                _journalingRefusal = true;
                try { await Save(State, [new Rejected(gate.Name, names, messages)]).ConfigureAwait(true); }
                finally { _journalingRefusal = false; }
                return "rejected";
            }, ct).ConfigureAwait(true);
            if (verdict is Verdict.Refused) { return verdict; }
            // Each gate of the chain sees what the gate before it allowed.
            if (verdict is Verdict.Allowed { Next: TState next }) { current = next; }
        }

        if (verdict is Verdict.Allowed { Next: TState provisional }) { _provisional.Add((signal, provisional, now)); }
        return verdict;
    }

    /// <inheritdoc />
    public async Task Committed(Signal signal, Verdict.Allowed allowed)
    {
        ArgumentNullException.ThrowIfNull(signal);
        ArgumentNullException.ThrowIfNull(allowed);
        var index = Provisional(signal);
        var next = index < 0 ? (TState)allowed.Next : _provisional[index].Next;
        if (index >= 0) { _provisional.RemoveAt(index); }
        await Save(next, [Changed(signal)]).ConfigureAwait(true);
        await PendingViews().ConfigureAwait(true);
    }

    /// <inheritdoc />
    public Task Aborted(Signal signal)
    {
        ArgumentNullException.ThrowIfNull(signal);
        var index = Provisional(signal);
        if (index >= 0) { _provisional.RemoveRange(index, _provisional.Count - index); }
        return Task.CompletedTask;
    }

    /// <summary>Marks the views to run over the new state once the saving synapse has reported its outcome; a journaled refusal leaves the state as it was and runs none.</summary>
    protected override void OnCommitted()
    {
        _viewsPending |= !_journalingRefusal;
    }

    private static string AliasOf(Signal signal) => signal.GetType().GetCustomAttribute<SignalAttribute>()?.Alias ?? signal.GetType().Name;

    // The oldest provisional entry for an equal signal (records compare by value, so a host's decoded copy finds its entry).
    private int Provisional(Signal signal) => _provisional.FindIndex(entry => entry.Signal.Equals(signal));

    private StateChanged Changed(Signal cause) => new(Id.ToString(), AliasOf(cause));

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
