using DigitalBrain.Abstractions;

namespace DigitalBrain.Time;

[GenerateSerializer]
[Alias("time.countdown-status")]
public enum CountdownStatus
{
    Unscheduled = 0,
    Scheduled = 1,
    Elapsed = 2,
    Cancelled = 3,
}

[GenerateSerializer]
[Alias("time.countdown-resolution")]
public enum CountdownResolution
{
    OnTime = 0,
    Recovered = 1,
}

[GenerateSerializer]
[Alias("time.countdown-snapshot")]
public sealed record CountdownSnapshot(
    [property: Id(0)] CountdownStatus Status,
    [property: Id(1)] long Generation,
    [property: Id(2)] long Revision,
    [property: Id(3)] NeuronId? Destination,
    [property: Id(4)] DateTimeOffset? ScheduledAt,
    [property: Id(5)] DateTimeOffset? DueAt,
    [property: Id(6)] TimeSpan? Duration);
