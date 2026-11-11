using DigitalBrain.Neurons;
using System.Reflection;
using DigitalBrain.Kernel;

namespace DigitalBrain.Runtime.Orleans.Synapses;

/// <summary>
/// Routes signals using the pure domain kernel rules in <see cref="ReactionTable"/>.
/// </summary>
internal static class ReactionDispatcher
{
    /// <summary>Runs the reaction of <paramref name="neuron"/> to <paramref name="signal"/>; returns false when it has none.</summary>
    public static Task<bool> React(Neuron neuron, Signal signal, CancellationToken cancellationToken)
        => ReactionTable.React(neuron, signal, cancellationToken);

    /// <summary>The reaction method of <paramref name="neuronClass"/> for <paramref name="signal"/>, or null.</summary>
    public static MethodInfo? Find(Type neuronClass, Type signal)
        => ReactionTable.Find(neuronClass, signal);
}
