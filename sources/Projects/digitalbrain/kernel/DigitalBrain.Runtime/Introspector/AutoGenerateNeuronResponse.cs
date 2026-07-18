using DigitalBrain.Runtime.Neurons;

namespace DigitalBrain.Runtime.Introspector;

[GenerateSerializer]
public sealed record AutoGenerateNeuronResponse([property: Id(1)] bool Success,
    [property: Id(2)] string InoSource,
    [property: Id(3)] string Error
) : Synapse;
