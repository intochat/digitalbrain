using DigitalBrain.Abstractions;

namespace DigitalBrain.Time;

[GenerateSerializer]
[Alias("time.countdown-elapsed")]
public sealed record CountdownElapsed(
    [property: Id(0)] NeuronId Countdown,
    [property: Id(1)] long Generation,
    [property: Id(2)] long Revision,
    [property: Id(3)] NeuronId Destination,
    [property: Id(4)] DateTimeOffset ScheduledAt,
    [property: Id(5)] DateTimeOffset DueAt,
    [property: Id(6)] DateTimeOffset ObservedAt,
    [property: Id(7)] CountdownResolution Resolution) : Synapse;
