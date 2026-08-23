using DigitalBrain.Abstractions;

using DigitalBrain.Abstractions.Neurons;
namespace DigitalBrain.Time;

[Alias("timer")]
public partial interface ITimer :
    INeuron,
    IHandle<StartTimer>,
    IHandle<CancelTimer>
{
    [Alias(nameof(Read))]
    Task<TimerSnapshot> Read();
}
