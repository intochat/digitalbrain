using DigitalBrain.Abstractions.Commands;
using DigitalBrain.Abstractions.Descriptors;
using DigitalBrain.Abstractions.Neurons;
using Orleans.Concurrency;

namespace DigitalBrain.Time;

[Alias("timer")]
public interface ITimer : INeuron
{
    /// <summary>Schedules a timer to elapse after the requested duration; the receipt is the generation the command was accepted against.</summary>
    [Alias("schedule")]
    [NeuronTool]
    Task<Accepted<TimerGeneration>> Schedule(ScheduleTimer command);

    /// <summary>Stops the scheduled timer.</summary>
    [Alias("stop")]
    [NeuronTool]
    Task<Accepted<TimerGeneration>> Stop(StopTimer command);

    /// <summary>Reads the timer's current snapshot.</summary>
    [ReadOnly]
    [Alias("read")]
    [NeuronTool(IsReadOnly = true)]
    Task<TimerSnapshot> Read();
}
