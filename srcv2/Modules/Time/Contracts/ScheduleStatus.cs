using DigitalBrain.Abstractions;

namespace DigitalBrain.Time;

[GenerateSerializer]
[Alias("time.schedule-status")]
public enum ScheduleStatus
{
    Idle = 0,
    Armed = 1,
    Cancelled = 2,
}

