using DigitalBrain.Abstractions;

using DigitalBrain.Abstractions.Neurons;
namespace DigitalBrain.Time;

// Recurring cadence with durable next-due and phase-preserving catch-up.
// Grain type "schedule"; instance name is free (often principal-scoped).
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
