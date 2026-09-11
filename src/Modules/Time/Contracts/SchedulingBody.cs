namespace DigitalBrain.Time;

[GenerateSerializer]
[Alias("time.scheduling-body")]
public sealed record SchedulingBody(
    [property: Id(0)] int DurationSeconds,
    [property: Id(1)] string Note);
