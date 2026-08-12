using DigitalBrain.Abstractions;

namespace DigitalBrain.Time;

// Due step: the schedule has crossed next-due (before the work tick).
[GenerateSerializer]
[Alias("time.schedule-due")]
public sealed record ScheduleDue(
    [property: Id(0)] NeuronId Schedule,
    [property: Id(1)] long Generation,
    [property: Id(2)] DateTimeOffset DueAt,
    [property: Id(3)] DateTimeOffset ObservedAt,
    [property: Id(4)] string Note,
    [property: Id(5)] ActorContext? OnBehalfOf = null) : Synapse;

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

[GenerateSerializer]
[Alias("time.schedule-armed")]
public sealed record ScheduleArmed(
    [property: Id(0)] CommandId CommandId,
    [property: Id(1)] NeuronId Schedule,
    [property: Id(2)] long Generation,
    [property: Id(3)] TimeSpan Period,
    [property: Id(4)] DateTimeOffset NextDue,
    [property: Id(5)] string Note,
    [property: Id(6)] ActorContext? OnBehalfOf = null) : Synapse;

[GenerateSerializer]
[Alias("time.schedule-cancelled")]
public sealed record ScheduleCancelled(
    [property: Id(0)] CommandId CommandId,
    [property: Id(1)] NeuronId Schedule,
    [property: Id(2)] long Generation) : Synapse;
