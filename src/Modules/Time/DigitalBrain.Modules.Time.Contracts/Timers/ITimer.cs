using DigitalBrain.Contracts;
namespace DigitalBrain.Time.Timers;

[Alias("timer")]
[Orleans.Metadata.DefaultGrainType("timer")]
public interface ITimer : INeuron
{
    // Replace the activation-local schedule. Null period means one shot.
    Task Start(TimeSpan dueTime, TimeSpan? period = null);
    // Stop scheduling. Already published signals are not recalled.
    Task Stop();
}