using DigitalBrain.Time;

namespace DigitalBrain.Scheduling;

/// <summary>The state of the time neuron.</summary>
/// <param name="Registrations">Who registered which schedule.</param>
public sealed record TimeState(IReadOnlyList<ScheduleRegistration> Registrations)
{
    /// <summary>No registrations.</summary>
    public TimeState() : this([]) { }
}
