using DigitalBrain.Runtime.Neurons;

namespace DigitalBrain.Runtime.Introspector;

[GenerateSerializer]
public sealed record AutoGenerateNeuronRequest([property: Id(1)] string Intent,
    [property: Id(2)] string SuggestedFqn,
    [property: Id(3)] string LlmModelKey
) : Synapse;
