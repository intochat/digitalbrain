using Orleans.Concurrency;

namespace Brain.Modules.Scheduling.Contracts;

[GenerateSerializer, Immutable]
public sealed record ScheduleSnapshot(
    [property: Id(0)] string ScheduleId,
    [property: Id(1)] string? Title,
    [property: Id(2)] DateTimeOffset? DueAtUtc,
    [property: Id(3)] string Status,
    [property: Id(4)] DateTimeOffset? TriggeredAtUtc);
