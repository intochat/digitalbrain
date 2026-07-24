using DigitalBrain.Abstractions;

namespace DigitalBrain.Time;

[GenerateSerializer]
[Alias("time.start-countdown")]
public sealed record StartCountdown(
    [property: Id(0)] CommandId CommandId,
    [property: Id(1)] TimeSpan Duration,
    [property: Id(2)] NeuronId Destination);

[GenerateSerializer]
[Alias("time.reschedule-countdown")]
public sealed record RescheduleCountdown(
    [property: Id(0)] CommandId CommandId,
    [property: Id(1)] long ExpectedRevision,
    [property: Id(2)] TimeSpan Duration);

[GenerateSerializer]
[Alias("time.cancel-countdown")]
public sealed record CancelCountdown(
    [property: Id(0)] CommandId CommandId,
    [property: Id(1)] long ExpectedRevision);

[GenerateSerializer]
[Alias("time.restart-countdown")]
public sealed record RestartCountdown(
    [property: Id(0)] CommandId CommandId,
    [property: Id(1)] TimeSpan Duration);
