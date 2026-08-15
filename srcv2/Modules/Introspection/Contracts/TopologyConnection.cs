using DigitalBrain.Abstractions;

namespace DigitalBrain.Introspection;

[GenerateSerializer]
[Alias("introspection.topology-connection")]
public sealed record TopologyConnection(
    [property: Id(0)] Guid ConnectionId,
    [property: Id(1)] string Source,
    [property: Id(2)] string SynapseAlias,
    [property: Id(3)] string Target,
    [property: Id(4)] string? Transform,
    [property: Id(5)] DateTimeOffset? ExpiresAt);

