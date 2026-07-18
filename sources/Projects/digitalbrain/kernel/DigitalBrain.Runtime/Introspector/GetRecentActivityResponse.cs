using DigitalBrain.Runtime.Neurons;

namespace DigitalBrain.Runtime.Introspector;

[GenerateSerializer]
public sealed record GetRecentActivityResponse([property: Id(1)] IReadOnlyList<Guid> CorrelationIds
) : Synapse;
