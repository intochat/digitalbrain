using DigitalBrain.Invocation;
using DigitalBrain.Kernel;

namespace DigitalBrain.Runtime.Orleans.Hosting;

/// <summary>
/// Routes calls using the pure domain kernel rules in <see cref="CallPolicy"/>.
/// </summary>
internal static class NeutralCallMapping
{
    /// <summary>Whether <paramref name="call"/> may run while another turn of its neuron is suspended.</summary>
    public static bool MayInterleave(Type neuronClass, BoundCall bound, RuntimeInvocation call)
        => CallPolicy.MayInterleave(neuronClass, bound, call);

    /// <summary>The class's <see cref="InterleavesWhenAttribute"/> predicate, or null when it declares none.</summary>
    public static Func<RuntimeInvocation, bool>? PredicateOf(Type neuronClass)
        => CallPolicy.PredicateOf(neuronClass);
}
