using DigitalBrain.Contracts;
namespace DigitalBrain.Time.Timers;

[Alias("timer")]
[Orleans.Metadata.DefaultGrainType("timer")]
public interface ITimer : INeuron
{
    /// <summary>Replace the activation-local schedule. Null period means one shot.</summary>
    Task Start(TimeSpan dueTime, TimeSpan? period = null);
    /// <summary>Stop scheduling. Already published signals are not recalled.</summary>
    Task Stop();
}