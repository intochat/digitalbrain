using DigitalBrain.Abstractions;

namespace DigitalBrain.Introspection;

[GenerateSerializer]
[Alias("introspection.topology-connection")]
public sealed record TopologyConnection(
    [property: Id(0)] string Source,
    [property: Id(1)] string SynapseAlias,
    [property: Id(2)] string Target);
