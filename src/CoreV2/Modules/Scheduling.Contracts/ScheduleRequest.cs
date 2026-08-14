using Orleans.Concurrency;

namespace Brain.Modules.Scheduling.Contracts;

[GenerateSerializer, Immutable]
public sealed record ScheduleRequest(
    [property: Id(0)] string Title,
    [property: Id(1)] DateTimeOffset DueAtUtc,
    [property: Id(2)] string IdempotencyKey);
