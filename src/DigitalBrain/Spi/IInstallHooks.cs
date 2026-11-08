using DigitalBrain.Time;

namespace DigitalBrain.Spi;

/// <summary>What a host does when a synapse app is installed or retired: wires its reaction edges and its schedules.</summary>
public interface IInstallHooks
{
    /// <summary>Ensures the state neuron <paramref name="state"/> receives <paramref name="signals"/> of its brain.</summary>
    /// <param name="state">The state neuron, <c>{brain}/{alias}/default</c>.</param>
    /// <param name="signals">The signal types its reactions take.</param>
    /// <param name="ct">Cancellation.</param>
    Task EnsureReactionEdges(NeuronId state, IReadOnlyList<Type> signals, CancellationToken ct);

    /// <summary>Ensures <paramref name="state"/> receives <paramref name="signals"/> and the signals named by <paramref name="signalAliases"/> of its brain.</summary>
    /// <param name="state">The state neuron, <c>{brain}/{alias}/default</c>.</param>
    /// <param name="signals">The signal types its typed reactions take.</param>
    /// <param name="signalAliases">The journal aliases its alias reactions are fed by.</param>
    /// <param name="ct">Cancellation.</param>
    /// <exception cref="NotSupportedException">Aliases are given and this host does not route by alias.</exception>
    Task EnsureReactionEdges(NeuronId state, IReadOnlyList<Type> signals, IReadOnlyList<string> signalAliases, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(signalAliases);
        return signalAliases.Count == 0
            ? EnsureReactionEdges(state, signals, ct)
            : throw new NotSupportedException($"{GetType().Name} does not route reactions by signal alias.");
    }

    /// <summary>Removes every reaction edge of <paramref name="state"/>.</summary>
    /// <param name="state">The state neuron.</param>
    /// <param name="ct">Cancellation.</param>
    Task RemoveReactionEdges(NeuronId state, CancellationToken ct);

    /// <summary>Registers <paramref name="schedules"/> for <paramref name="state"/>, replacing any it had.</summary>
    /// <param name="state">The state neuron.</param>
    /// <param name="schedules">The schedules it runs on.</param>
    /// <param name="ct">Cancellation.</param>
    Task RegisterSchedules(NeuronId state, IReadOnlyList<Schedule> schedules, CancellationToken ct);

    /// <summary>Removes every schedule of <paramref name="state"/>.</summary>
    /// <param name="state">The state neuron.</param>
    /// <param name="ct">Cancellation.</param>
    Task UnregisterSchedules(NeuronId state, CancellationToken ct);

    /// <summary>Checks that <paramref name="binding"/> resolves <paramref name="slot"/> of <paramref name="state"/> on this host.</summary>
    /// <param name="state">The state neuron.</param>
    /// <param name="slot">The slot being bound.</param>
    /// <param name="binding">The binding the install request gives the slot.</param>
    /// <param name="ct">Cancellation.</param>
    /// <returns>Null when the binding resolves; otherwise why it does not.</returns>
    Task<string?> ValidateBinding(NeuronId state, SlotDescriptor slot, string binding, CancellationToken ct);
}
