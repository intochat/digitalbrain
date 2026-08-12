using DigitalBrain.Abstractions;

namespace DigitalBrain.Time;

[GenerateSerializer]
[Alias("time.arm-schedule")]
public sealed record ArmSchedule(
    [property: Id(0)] CommandId CommandId,
    // Cadence in seconds (gate math uses 5 minutes = 300; short periods OK for live verify).
    [property: Id(1)] int PeriodSeconds,
    [property: Id(2)] string Note,
    // Who the tick acts as (Wave 5 OnBehalfOf). Null → VerifiedActor.Current at arm time.
    [property: Id(3)] ActorContext? OnBehalfOf = null) : RequestSynapse<ScheduleArmed>;

[GenerateSerializer]
[Alias("time.cancel-schedule")]
public sealed record CancelSchedule(
    [property: Id(0)] CommandId CommandId) : RequestSynapse<ScheduleCancelled>;

// Verification/ops: backdate NextDue by MissedPeriods and run one phase-preserving catch-up.
// Same math as silo downtime; used when cluster restart is not available in-session.
[GenerateSerializer]
[Alias("time.force-schedule-catch-up")]
public sealed record ForceScheduleCatchUp(
    [property: Id(0)] CommandId CommandId,
    [property: Id(1)] int MissedPeriods = 4) : RequestSynapse<ScheduleTick>;

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

[GenerateSerializer]
[Alias("time.schedule-status")]
public enum ScheduleStatus
{
    Idle = 0,
    Armed = 1,
    Cancelled = 2,
}

[GenerateSerializer]
[Alias("time.schedule-resolution")]
public enum ScheduleResolution
{
    OnTime = 0,
    Recovered = 1,
}
