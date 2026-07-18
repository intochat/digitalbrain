using DigitalBrain.Runtime.Neurons;

namespace DigitalBrain.Runtime.Dynamic;

[GenerateSerializer]
public sealed record NeuronCreated([property: Id(1)] string NeuronId,
    [property: Id(2)] string Status,
    [property: Id(3)] string Message
) : Synapse;
