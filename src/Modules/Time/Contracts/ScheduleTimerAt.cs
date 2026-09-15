using DigitalBrain.Abstractions.Commands;
using DigitalBrain.Abstractions.Identity;

namespace DigitalBrain.Time;

/// <summary>Schedules a timer at an absolute Unix timestamp, including overdue recovery.</summary>
[GenerateSerializer, Alias("time.schedule-timer-at")]
public sealed record ScheduleTimerAt(CommandId Id,
    [property: Id(0)] long DueUnixSeconds,
    [property: Id(1)] string Note) : Command(Id);
