using DigitalBrain.Runtime.Neurons;

namespace DigitalBrain.Runtime.Introspector;

[GenerateSerializer]
public sealed record ExplainDecisionResponse([property: Id(1)] string NaturalLanguageAnswer,
    [property: Id(2)] IReadOnlyList<Guid> CitedCorrelationIds
) : Synapse;
