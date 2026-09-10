using DigitalBrain.Abstractions.Commands;
using DigitalBrain.Abstractions.Neurons;
using Orleans.Concurrency;

namespace DigitalBrain.Time;

[Alias("timer")]
public interface ITimer : INeuron
{
    /// <summary>Schedules a timer to elapse after the requested duration.</summary>
    [Alias("schedule")]
    Task<Accepted<TimerGeneration>> Schedule(ScheduleTimer command);

    /// <summary>Stops the scheduled timer.</summary>
    [Alias("stop")]
    Task<Accepted<TimerGeneration>> Stop(StopTimer command);

    /// <summary>Reads the timer's current snapshot.</summary>
    [ReadOnly]
    [Alias("read")]
    Task<TimerSnapshot> Read();
}
