using DigitalBrain.Abstractions;

namespace DigitalBrain.Time;

// Recurring cadence with durable next-due and phase-preserving catch-up.
// Grain type "schedule"; instance name is free (often principal-scoped).
[ClientEntryPoint]
[Alias("schedule")]
public partial interface ISchedule :
    INeuron,
    IHandle<ArmSchedule>,
    IHandle<CancelSchedule>,
    IHandle<ForceScheduleCatchUp>
{
    const string GrainTypeName = "schedule";

    [Alias(nameof(Read))]
    Task<ScheduleSnapshot> Read();
}
