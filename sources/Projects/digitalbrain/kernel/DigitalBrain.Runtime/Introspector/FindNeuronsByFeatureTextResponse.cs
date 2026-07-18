using DigitalBrain.Runtime.Neurons;

namespace DigitalBrain.Runtime.Introspector;

[GenerateSerializer]
public sealed record FindNeuronsByFeatureTextResponse([property: Id(1)] IReadOnlyList<NeuronRef> Neurons
) : Synapse;
