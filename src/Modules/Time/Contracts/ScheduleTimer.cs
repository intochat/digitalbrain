using DigitalBrain.Abstractions.Commands;
using DigitalBrain.Abstractions.Identity;

namespace DigitalBrain.Time;

/// <summary>A timer duration and note to schedule.</summary>
[GenerateSerializer]
[Alias("time.schedule-timer")]
public sealed record ScheduleTimer(
    CommandId Id,
    [property: Id(0)] int DurationSeconds,
    [property: Id(1)] string Note) : Command(Id);
