using DigitalBrain.Runtime.Neurons;

namespace DigitalBrain.Runtime.Introspector;

[GenerateSerializer]
public sealed record TraceCorrelationRequest([property: Id(1)] Guid TargetCorrelationId
) : Synapse;
