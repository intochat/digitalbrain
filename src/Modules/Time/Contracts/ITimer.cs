using DigitalBrain.Abstractions;

namespace DigitalBrain.Time;

[ClientEntryPoint]
[Alias("timer")]
public partial interface ITimer :
    INeuron,
    IHandle<StartTimer>,
    IHandle<CancelTimer>
{
    [Alias(nameof(Read))]
    Task<TimerSnapshot> Read();
}
