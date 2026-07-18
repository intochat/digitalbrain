using DigitalBrain.Runtime.Neurons;

namespace DigitalBrain.Runtime.Introspector;

[GenerateSerializer]
public sealed record FindRootSynapseResponse([property: Id(1)] Synapse? Root
) : Synapse;
