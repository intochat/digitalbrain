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

