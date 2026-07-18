using DigitalBrain.Runtime.Neurons;

namespace DigitalBrain.Runtime.Visualization;

[GenerateSerializer]
public sealed record CancelCorrelation([property: Id(1)] Guid TargetCorrelationId
) : Synapse;
