using System.Diagnostics;
using System.Reflection;
using System.Runtime.ExceptionServices;
using DigitalBrain.Rules;
using Verdict = DigitalBrain.Rules.Check;
using DigitalBrain.Spi;

namespace DigitalBrain;

/// <summary>The neuron that runs the gate, reaction and view synapses of one <c>[State]</c> record.</summary>
/// <typeparam name="TState">The state record.</typeparam>
public sealed class StateNeuron<TState> : Neuron<TState>, ISynapseReceiver where TState : class, new()
{
    private const string GatePlaceholder = "gate";
    private readonly string _alias;
    private readonly IReadOnlyList<SynapseDescriptor> _synapses;
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
            await Run(reaction, signal, async result =>
            {
                if (ReferenceEquals(result, State)) { return "unchanged"; }
                if (result is Reaction<TState> r)
                {
                    await Save(r.Next, r.Signals.Count == 0 ? [signal] : r.Signals).ConfigureAwait(true);
                }
                else
                {
                    await Save((TState)result!, [signal]).ConfigureAwait(true);
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
        var verdict = Verdict.Allow(State);
        foreach (var gate in Matching(SynapseKind.Gate, signal))
        {
            await Run(gate, signal, async result =>
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
        }

        return verdict;
    }

    /// <inheritdoc />
    public async Task Committed(Signal signal, Verdict.Allowed allowed)
    {
        ArgumentNullException.ThrowIfNull(signal);
        ArgumentNullException.ThrowIfNull(allowed);
        await Save((TState)allowed.Next, [signal]).ConfigureAwait(true);
        await PendingViews().ConfigureAwait(true);
    }

    /// <summary>Marks the views to run over the new state once the saving synapse has reported its outcome; a journaled refusal leaves the state as it was and runs none.</summary>
    protected override void OnCommitted()
    {
        _viewsPending |= !_journalingRefusal;
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
            await Run(view, null, async result =>
            {
                await PublishEphemeral(new ViewChanged(Id.ToString(), result!)).ConfigureAwait(true);
                return "viewed";
            }, CancellationToken.None).ConfigureAwait(true);
        }
    }

    private IEnumerable<SynapseDescriptor> Matching(SynapseKind kind, Signal signal) =>
        _synapses.Where(d => d.Kind == kind && d.Signal!.IsInstanceOfType(signal));

    private async Task Run(SynapseDescriptor synapse, Signal? signal, Func<object?, Task<string>> settle, CancellationToken ct)
    {
        var neuron = Id.ToString();
        var arriving = signal is null ? _alias : signal.GetType().GetCustomAttribute<SignalAttribute>()?.Alias ?? signal.GetType().Name;
        await PublishEphemeral(new SynapseStarted(neuron, synapse.Name, arriving)).ConfigureAwait(true);
        var clock = Stopwatch.StartNew();
        string outcome;
        try
        {
            outcome = await settle(await Invoke(synapse, signal, ct).ConfigureAwait(true)).ConfigureAwait(true);
        }
        catch
        {
            await PublishEphemeral(new SynapseFinished(neuron, synapse.Name, "failed", clock.Elapsed.TotalMilliseconds)).ConfigureAwait(true);
            throw;
        }

        await PublishEphemeral(new SynapseFinished(neuron, synapse.Name, outcome, clock.Elapsed.TotalMilliseconds)).ConfigureAwait(true);
    }

    private async Task<object?> Invoke(SynapseDescriptor synapse, Signal? signal, CancellationToken ct)
    {
        var arguments = synapse.Function.GetParameters()
            .Select(p => p.ParameterType == typeof(CancellationToken) ? ct : typeof(Signal).IsAssignableFrom(p.ParameterType) ? signal : (object)State)
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
