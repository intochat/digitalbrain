using DigitalBrain.Runtime.Neurons;

namespace DigitalBrain.Runtime.Dynamic;

[GenerateSerializer]
public sealed record PlanNeuronRequest([property: Id(1)] string Intent,
    [property: Id(2)] string? LastError,
    [property: Id(3)] int Attempt,
    [property: Id(4)] string SuggestedNeuronId,
    [property: Id(5)] string? PinnedLlmModel = null
) : Synapse;
