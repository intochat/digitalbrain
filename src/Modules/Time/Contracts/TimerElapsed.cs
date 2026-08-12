using DigitalBrain.Abstractions;

namespace DigitalBrain.Time;

[GenerateSerializer]
[Alias("time.timer-elapsed")]
public sealed record TimerElapsed(
    [property: Id(0)] NeuronId Timer,
    [property: Id(1)] long Generation,
    [property: Id(2)] DateTimeOffset ScheduledAt,
    [property: Id(3)] DateTimeOffset DueAt,
    [property: Id(4)] DateTimeOffset ObservedAt,
    [property: Id(5)] TimerResolution Resolution,
    [property: Id(6)] string Note) : Synapse;

