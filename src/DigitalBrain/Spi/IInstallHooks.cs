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
}
