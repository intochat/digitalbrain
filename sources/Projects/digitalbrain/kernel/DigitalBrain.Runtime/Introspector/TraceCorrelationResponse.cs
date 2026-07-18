using DigitalBrain.Runtime.Neurons;

namespace DigitalBrain.Runtime.Introspector;

[GenerateSerializer]
public sealed record TraceCorrelationResponse([property: Id(1)] IReadOnlyList<Synapse> Chain
) : Synapse;
