using DigitalBrain.Abstractions;

namespace DigitalBrain.Time;

[GenerateSerializer]
[Alias("time.timer-status")]
public enum TimerStatus
{
    Unscheduled = 0,
    Scheduled = 1,
    Elapsed = 2,
    Cancelled = 3,
}

[GenerateSerializer]
[Alias("time.timer-resolution")]
public enum TimerResolution
{
    OnTime = 0,
    Recovered = 1,
}

[GenerateSerializer]
[Alias("time.timer-snapshot")]
public sealed record TimerSnapshot(
    [property: Id(0)] TimerStatus Status,
    [property: Id(1)] long Generation,
    [property: Id(2)] DateTimeOffset? ScheduledAt,
    [property: Id(3)] DateTimeOffset? DueAt,
    [property: Id(4)] TimeSpan? Duration,
    [property: Id(5)] string? Note);
