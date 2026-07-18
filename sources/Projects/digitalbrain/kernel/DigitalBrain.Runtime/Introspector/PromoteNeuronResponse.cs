using DigitalBrain.Runtime.Neurons;

namespace DigitalBrain.Runtime.Introspector;

[GenerateSerializer]
public sealed record PromoteNeuronResponse([property: Id(1)] bool Success,
    [property: Id(2)] string Version,
    [property: Id(3)] string Message
) : Synapse;
