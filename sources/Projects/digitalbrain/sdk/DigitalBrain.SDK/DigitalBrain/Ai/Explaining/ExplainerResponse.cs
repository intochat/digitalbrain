using DigitalBrain.Runtime.Neurons;

namespace DigitalBrain.SDK.DigitalBrain.Ai.Explaining;

[GenerateSerializer]
public sealed record ExplainerResponse([property: Id(1)] string NaturalLanguageAnswer,
    [property: Id(2)] IReadOnlyList<Guid> CitedCorrelationIds,
    [property: Id(3)] IReadOnlyList<string> ToolCallTrace
) : Synapse;
