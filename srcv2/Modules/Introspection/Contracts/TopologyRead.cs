using DigitalBrain.Abstractions;

namespace DigitalBrain.Introspection;

[GenerateSerializer]
[Alias("introspection.topology-read")]
public sealed record TopologyRead(
    [property: Id(0)] CommandId CommandId,
    [property: Id(1)] IReadOnlyList<string> Modules,
    [property: Id(2)] IReadOnlyList<TopologyNeuron> Neurons,
    [property: Id(3)] DateTimeOffset ObservedAt,
    [property: Id(4)] IReadOnlyList<TopologyConnection> Connections,
    [property: Id(5)] IReadOnlyList<TopologyBroadcastRoute> BroadcastRoutes) : Synapse;

