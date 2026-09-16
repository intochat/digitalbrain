using DigitalBrain.Abstractions.Identity;

namespace DigitalBrain.Abstractions.Signals;

[GenerateSerializer]
[Alias("db.v3.announcement")]
public sealed record Announcement(
    [property: Id(0)] SignalId Id,
    [property: Id(1)] Signal Signal,
    [property: Id(2)] NeuronId? To,
    [property: Id(3)] CorrelationId Correlation,
    [property: Id(4)] SignalId Causation);
