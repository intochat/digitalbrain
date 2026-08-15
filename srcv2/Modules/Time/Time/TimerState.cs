using DigitalBrain.Abstractions;
using Orleans.Serialization;

namespace DigitalBrain.Time;

[GenerateSerializer]
[Alias("time.timer-state")]
internal sealed record TimerState(
    [property: Id(0)] TimerStatus Status,
    [property: Id(1)] long Generation,
    [property: Id(2)] DateTimeOffset ScheduledAt,
    [property: Id(3)] DateTimeOffset DueAt,
    [property: Id(4)] TimeSpan Duration,
    [property: Id(5)] string Note,
    [property: Id(6)] string? ActiveReminderName);
