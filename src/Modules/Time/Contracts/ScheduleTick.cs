using DigitalBrain.Abstractions;

namespace DigitalBrain.Time;

// Tick step: one phase-preserving catch-up emission for N collapsed periods.
[GenerateSerializer]
[Alias("time.schedule-tick")]
public sealed record ScheduleTick(
    [property: Id(0)] NeuronId Schedule,
    [property: Id(1)] long Generation,
    [property: Id(2)] DateTimeOffset DueAt,
    [property: Id(3)] DateTimeOffset ObservedAt,
    [property: Id(4)] DateTimeOffset NextDue,
    [property: Id(5)] ScheduleResolution Resolution,
    [property: Id(6)] int CollapsedPeriods,
    [property: Id(7)] string Note,
    [property: Id(8)] ActorContext? OnBehalfOf = null) : Synapse;

