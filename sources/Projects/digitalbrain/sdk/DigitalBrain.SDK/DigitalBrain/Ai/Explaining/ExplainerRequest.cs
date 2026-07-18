using DigitalBrain.Runtime.Neurons;

namespace DigitalBrain.SDK.DigitalBrain.Ai.Explaining;

[GenerateSerializer]
public sealed record ExplainerRequest([property: Id(1)] string NaturalLanguageQuery,
    [property: Id(2)] string UserId
) : Synapse;
