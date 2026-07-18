using DigitalBrain.Runtime.Neurons;

namespace DigitalBrain.Runtime.Introspector;

[GenerateSerializer]
public sealed record PromoteNeuronRequest([property: Id(1)] string Fqn,
    [property: Id(2)] string InoSource
) : Synapse;
