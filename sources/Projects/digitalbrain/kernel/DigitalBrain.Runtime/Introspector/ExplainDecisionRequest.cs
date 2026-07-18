using DigitalBrain.Runtime.Neurons;

namespace DigitalBrain.Runtime.Introspector;

[GenerateSerializer]
public sealed record ExplainDecisionRequest([property: Id(1)] string NaturalLanguageQuery,
    [property: Id(2)] string UserId
) : Synapse;
