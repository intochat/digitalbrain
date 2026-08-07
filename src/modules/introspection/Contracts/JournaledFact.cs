using DigitalBrain.Abstractions;

namespace DigitalBrain.Introspection;

[GenerateSerializer]
[Alias("introspection.journaled-fact")]
public sealed record JournaledFact(
    [property: Id(0)] long Sequence,
    [property: Id(1)] string Synapse,
    [property: Id(2)] string Caller,
    [property: Id(3)] string Correlation,
    [property: Id(4)] DateTimeOffset Timestamp);
