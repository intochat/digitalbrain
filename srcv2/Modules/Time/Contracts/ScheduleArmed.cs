using DigitalBrain.Abstractions;

namespace DigitalBrain.Time;

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

