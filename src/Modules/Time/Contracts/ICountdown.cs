using DigitalBrain.Abstractions;

namespace DigitalBrain.Time;

[ClientEntryPoint]
[Alias("DigitalBrain.Time.ICountdown")]
public interface ICountdown : INeuron
{
    [Alias(nameof(Start))]
    Task<CountdownSnapshot> Start(StartCountdown command);

    [Alias(nameof(Reschedule))]
    Task<CountdownSnapshot> Reschedule(RescheduleCountdown command);

    [Alias(nameof(Cancel))]
    Task<CountdownSnapshot> Cancel(CancelCountdown command);

    [Alias(nameof(Restart))]
    Task<CountdownSnapshot> Restart(RestartCountdown command);

    [Alias(nameof(Read))]
    Task<CountdownSnapshot> Read();
}
