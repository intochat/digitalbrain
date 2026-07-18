using DigitalBrain.Runtime.Neurons;

namespace DigitalBrain.Runtime.Introspector;

[GenerateSerializer]
public sealed record FindNeuronsByFeatureTextRequest([property: Id(1)] string Query,
    [property: Id(2)] int Limit
) : Synapse;
