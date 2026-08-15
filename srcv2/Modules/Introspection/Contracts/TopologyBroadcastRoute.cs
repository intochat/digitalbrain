using DigitalBrain.Abstractions;

namespace DigitalBrain.Introspection;

[GenerateSerializer]
[Alias("introspection.topology-broadcast-route")]
public sealed record TopologyBroadcastRoute(
    [property: Id(0)] string SynapseAlias,
    [property: Id(1)] string HandlerGrainType);

