using DigitalBrain.Abstractions;

namespace DigitalBrain.Time;

[GenerateSerializer]
[Alias("time.timer-resolution")]
public enum TimerResolution
{
    OnTime = 0,
    Recovered = 1,
}

