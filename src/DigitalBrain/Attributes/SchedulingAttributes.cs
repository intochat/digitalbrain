using DigitalBrain.Neurons;

namespace DigitalBrain;

/// <summary>
/// One contract a neuron class calls through <c>Get&lt;T&gt;</c>, recorded on the assembly at build time by the DigitalBrain source generator
/// so the brain graph can draw <c>calls</c> edges and find call cycles without loading code.
/// </summary>
/// <param name="from">The calling neuron's type (its <c>[Neuron]</c> name) or app id.</param>
/// <param name="to">The called contract's alias (its <c>[NeuronAlias]</c>) or type name.</param>
[AttributeUsage(AttributeTargets.Assembly, AllowMultiple = true, Inherited = false)]
public sealed class CallsAttribute(string from, string to) : Attribute
{
    /// <summary>The calling neuron's type or app id.</summary>
    public string From { get; } = from;

    /// <summary>The called contract's alias or type name.</summary>
    public string To { get; } = to;
}
