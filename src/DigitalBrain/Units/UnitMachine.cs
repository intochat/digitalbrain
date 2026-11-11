#pragma warning disable CA1822 // Mark members as static

using System.Text.Json;
using DigitalBrain.Invocation;

namespace DigitalBrain.Units;

/// <summary>What one turn has decided so far. The host commits it as ONE journal entry.</summary>
internal sealed record UnitStep<TState>(TState State, PublishedView? View, bool Changed, IReadOnlyList<Signal> Signals) where TState : class
{
    public StoredUnit<TState> Stored => new(State, View);
}

internal enum ViewOutcome
{
    Stored,
    NotAView,
    DeclaresBy
}

/// <summary>The pure rules of a unit. No context, no journal, no clock; no function is called here: results come in, a step goes out.</summary>
internal sealed class UnitMachine<TState>(string alias, string neuron) where TState : class, new()
{
    public UnitStep<TState> Start(StoredUnit<TState> stored) =>
        new(stored.State ?? new TState(), stored.View, Changed: false, Signals: []);

    public UnitStep<TState> Reacted(UnitStep<TState> step, Signal cause, object? result) => result switch
    {
        Reaction<TState> { Next: null } =>
            throw new InvalidOperationException($"A reaction of '{alias}' returned a Reaction with no next state."),
        Reaction<TState> said =>
            step with
            {
                State = said.Next,
                Changed = true,
                Signals = said.Signals.Count == 0 ? Silent(step, cause) : [.. step.Signals, .. said.Signals]
            },
        TState same when ReferenceEquals(same, step.State) => step,
        TState next =>
            step with { State = next, Changed = true, Signals = Silent(step, cause) },
        _ =>
            throw new InvalidOperationException(
                $"A reaction of '{alias}' returned {result?.GetType().FullName ?? "null"}, not {typeof(TState).Name} or Reaction<{typeof(TState).Name}>.")
    };

    public (UnitStep<TState> Step, ViewOutcome Outcome) Viewed(UnitStep<TState> step, object? result)
    {
        if (result is null || result.GetType().FullName != ViewNodes.ViewType)
        {
            return (step, ViewOutcome.NotAView);
        }

        var type = result.GetType();
        var wire = JsonSerializer.SerializeToElement(result, type, RuntimeProtocol.OptionsFor(type));
        if (ViewNodes.DeclaresBy(wire))
        {
            return (step, ViewOutcome.DeclaresBy);
        }

        var view = new PublishedView(alias, (step.View?.Generation ?? 0) + 1, wire);
        return (step with { View = view, Signals = [.. step.Signals, new ViewChanged(alias, view.Generation)] }, ViewOutcome.Stored);
    }

    /// <exception cref="ActivationRefusedException">The view is stale, the node is unknown or emits nothing, or its emit names another owner.</exception>
    public EmittedSignal Press(PublishedView? view, string claimed, long generation, string nodeKey, string? fieldValue, string? by)
    {
        if (view is null || !string.Equals(view.Alias, claimed, StringComparison.Ordinal) || view.Generation != generation)
        {
            throw new ActivationRefusedException(ActivationRefusedException.Stale);
        }

        var node = ViewNodes.Find(view.View, nodeKey) ?? throw new ActivationRefusedException(ActivationRefusedException.UnknownNode);
        var emit = ViewNodes.EmitOf(node) ?? throw new ActivationRefusedException(ActivationRefusedException.NoEmit);
        if (!string.Equals(emit.Owner, claimed, StringComparison.Ordinal))
        {
            throw new ActivationRefusedException(ActivationRefusedException.ForeignOwner);
        }

        return new EmittedSignal(emit.Signal, ViewNodes.Payload(emit.Args, fieldValue, by));
    }

    private IReadOnlyList<Signal> Silent(UnitStep<TState> step, Signal cause)
    {
        var changed = new StateChanged(neuron, SignalAliases.Of(cause));
        return step.Signals.Contains(changed) ? step.Signals : [.. step.Signals, changed];
    }
}
#pragma warning restore CA1822
