namespace DigitalBrain.Time;

[GenerateSerializer]
[Alias("time.scheduling-body")]
public sealed record SchedulingBody(
    [property: Id(0)] long Generation,
    [property: Id(1)] DateTimeOffset ScheduledAt,
    [property: Id(2)] DateTimeOffset DueAt,
    [property: Id(3)] int DurationSeconds,
    [property: Id(4)] string Note);
