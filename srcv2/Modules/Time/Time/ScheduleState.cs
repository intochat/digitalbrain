using DigitalBrain.Abstractions;
using Orleans.Serialization;

namespace DigitalBrain.Time;

[GenerateSerializer]
[Alias("time.schedule-state")]
internal sealed record ScheduleState(
    [property: Id(0)] ScheduleStatus Status,
    [property: Id(1)] long Generation,
    [property: Id(2)] TimeSpan Period,
    [property: Id(3)] DateTimeOffset NextDue,
    [property: Id(4)] DateTimeOffset? LastTickAt,
    [property: Id(5)] string Note,
    [property: Id(6)] Guid? OnBehalfOfPrincipal,
    [property: Id(7)] string? OnBehalfOfUsername,
    [property: Id(8)] string? ActiveReminderName,
    [property: Id(9)] int LastCollapsedPeriods,
    [property: Id(10)] ScheduleResolution? LastResolution);
