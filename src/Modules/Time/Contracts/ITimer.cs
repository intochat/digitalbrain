using DigitalBrain.Abstractions.Commands;
using DigitalBrain.Abstractions.Neurons;
using Orleans.Concurrency;

namespace DigitalBrain.Time;

[Alias("timer")]
public interface ITimer : INeuron
{
    /// <summary>Schedules a timer to elapse after the requested duration; the receipt is the generation the command was accepted against.</summary>
    [Alias("schedule")]
    Task<Accepted<TimerGeneration>> Schedule(ScheduleTimer command);

    /// <summary>Schedules an absolute deadline; overdue work fires as soon as recovered.</summary>
    [Alias("schedule-at")]
    Task<Accepted<TimerGeneration>> ScheduleAt(ScheduleTimerAt command);

    /// <summary>Stops the scheduled timer.</summary>
    [Alias("stop")]
    Task<Accepted<TimerGeneration>> Stop(StopTimer command);

    /// <summary>Reads the timer's current snapshot.</summary>
    [ReadOnly]
    [Alias("read")]
    Task<TimerSnapshot> Read();
}
