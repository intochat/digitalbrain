using DigitalBrain.Abstractions;

namespace DigitalBrain.Time;

[GenerateSerializer]
[Alias("time.schedule-resolution")]
public enum ScheduleResolution
{
    OnTime = 0,
    Recovered = 1,
}

