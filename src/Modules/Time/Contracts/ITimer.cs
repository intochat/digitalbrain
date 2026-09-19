using DigitalBrain.Contracts;
namespace DigitalBrain.Time;

[Alias("timer")]
[Orleans.Metadata.DefaultGrainType("timer")]
public interface ITimer : INeuron
{
    Task<TimerSnapshot> Schedule(int durationSeconds, string note, long? expectedGeneration = null);
    Task<TimerSnapshot> Stop();
    Task<TimerSnapshot> Read();
}
