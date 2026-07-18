using DigitalBrain.V2.Core.Synapses;

namespace Greeter.Contracts;

[GenerateSerializer]
public sealed record Hello([property: Id(0)] string Name) : Synapse;

[GenerateSerializer]
public sealed record Announce([property: Id(0)] string Name) : Synapse;

[GenerateSerializer]
public sealed record Announced([property: Id(0)] string Name) : Synapse;

[GenerateSerializer]
public sealed record BystanderHeardHello([property: Id(0)] string Name) : Synapse;

[GenerateSerializer]
public sealed record BystanderHeardAnnounced([property: Id(0)] string Name) : Synapse;
