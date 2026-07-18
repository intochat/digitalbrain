using DigitalBrain.Runtime.Neurons;

namespace DigitalBrain.Runtime.Introspector;

[GenerateSerializer]
public sealed record FindRootSynapseRequest([property: Id(1)] Guid SynapseIdToTrace
) : Synapse;
