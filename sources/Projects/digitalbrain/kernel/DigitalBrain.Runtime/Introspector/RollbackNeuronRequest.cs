using DigitalBrain.Runtime.Neurons;

namespace DigitalBrain.Runtime.Introspector;

[GenerateSerializer]
public sealed record RollbackNeuronRequest([property: Id(1)] string Fqn,
    [property: Id(2)] int TargetVersion
) : Synapse;
