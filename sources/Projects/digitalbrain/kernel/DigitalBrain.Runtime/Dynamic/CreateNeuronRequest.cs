using DigitalBrain.Runtime.Neurons;

namespace DigitalBrain.Runtime.Dynamic;

[GenerateSerializer]
public sealed record CreateNeuronRequest([property: Id(1)] string Intent,
    [property: Id(2)] string SuggestedNeuronId
) : Synapse;
