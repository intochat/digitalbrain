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

