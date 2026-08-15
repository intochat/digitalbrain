using DigitalBrain.Abstractions;

namespace DigitalBrain.Time;

[GenerateSerializer]
[Alias("time.timer-status")]
public enum TimerStatus
{
    Unscheduled = 0,
    Scheduled = 1,
    Elapsed = 2,
    Cancelled = 3,
}

