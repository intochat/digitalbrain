using System.ComponentModel;
using DigitalBrain.Abstractions;

namespace DigitalBrain.Introspection;

[GenerateSerializer]
[Alias("introspection.topology-neuron")]
public sealed record TopologyNeuron(
    [property: Id(0)] string Id,
    [property: Id(1)] string GrainType,
    [property: Id(2)] string Identity,
    [property: Id(3)] string Placement);

[GenerateSerializer]
[Alias("introspection.topology-read")]
[Description("The modules this deployment composed and the owner's currently activated neurons")]
public sealed record TopologyRead(
    [property: Id(0)] CommandId CommandId,
    [property: Id(1)] IReadOnlyList<string> Modules,
    [property: Id(2)] IReadOnlyList<TopologyNeuron> Neurons,
    [property: Id(3)] DateTimeOffset ObservedAt) : Synapse;
