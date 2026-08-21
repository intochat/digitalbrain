using DigitalBrain.Abstractions;

using DigitalBrain.Abstractions.Identity;
namespace DigitalBrain.Time;

[GenerateSerializer]
[Alias("time.schedule-snapshot")]
public sealed record ScheduleSnapshot(
    [property: Id(0)] ScheduleStatus Status,
    [property: Id(1)] long Generation,
    [property: Id(2)] TimeSpan? Period,
    [property: Id(3)] DateTimeOffset? NextDue,
    [property: Id(4)] DateTimeOffset? LastTickAt,
    [property: Id(5)] string? Note,
    [property: Id(6)] PrincipalId? OnBehalfOf,
    [property: Id(7)] int LastCollapsedPeriods,
    [property: Id(8)] ScheduleResolution? LastResolution);

