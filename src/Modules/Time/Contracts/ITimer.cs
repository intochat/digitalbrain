using System.ComponentModel;
using DigitalBrain.Abstractions;

namespace DigitalBrain.Time;

[ClientEntryPoint]
[Alias("timer")]
[Description("Owner timer neuron: arms a countdown and fires its note when due")]
public partial interface ITimer :
    INeuron,
    IHandle<StartTimer>,
    IHandle<CancelTimer>,
    IEmit<TimerScheduled>,
    IEmit<TimerElapsed>,
    IEmit<TimerCancelled>
{
    [Alias(nameof(Read))]
    Task<TimerSnapshot> Read();
}
