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
[Alias("introspection.topology-connection")]
public sealed record TopologyConnection(
    [property: Id(0)] Guid ConnectionId,
    [property: Id(1)] string Source,
    [property: Id(2)] string SynapseAlias,
    [property: Id(3)] string Target,
    [property: Id(4)] string? Transform,
    [property: Id(5)] DateTimeOffset? ExpiresAt);

[GenerateSerializer]
[Alias("introspection.topology-broadcast-route")]
public sealed record TopologyBroadcastRoute(
    [property: Id(0)] string SynapseAlias,
    [property: Id(1)] string HandlerGrainType);

[GenerateSerializer]
[Alias("introspection.topology-read")]
[Description("The modules this deployment composed, the owner's activated neurons, the live synapse connections, and the compiled broadcast tier")]
public sealed record TopologyRead(
    [property: Id(0)] CommandId CommandId,
    [property: Id(1)] IReadOnlyList<string> Modules,
    [property: Id(2)] IReadOnlyList<TopologyNeuron> Neurons,
    [property: Id(3)] DateTimeOffset ObservedAt,
    [property: Id(4)] IReadOnlyList<TopologyConnection> Connections,
    [property: Id(5)] IReadOnlyList<TopologyBroadcastRoute> BroadcastRoutes) : Synapse;
