using DigitalBrain.Abstractions;

namespace DigitalBrain.Introspection;

[GenerateSerializer]
[Alias("introspection.topology-neuron")]
public sealed record TopologyNeuron(
    [property: Id(0)] string Id,
    [property: Id(1)] string GrainType,
    [property: Id(2)] string Identity,
    [property: Id(3)] string Placement);

