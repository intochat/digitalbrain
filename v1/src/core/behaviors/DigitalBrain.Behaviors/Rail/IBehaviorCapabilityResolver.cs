namespace DigitalBrain.Behaviors;

using DigitalBrain.Abstractions;

public interface IBehaviorCapabilityResolver
{
    [System.Diagnostics.CodeAnalysis.SuppressMessage(
        "Naming",
        "CA1716:Identifiers should not match keywords",
        Justification = "Get matches IBehaviorContext.Get for resolving an approved module neuron.")]
    TContract Get<TContract>(string name)
        where TContract : class, INeuron;
}
