using System.ComponentModel;
using DigitalBrain.Abstractions;

namespace DigitalBrain.Tasks;

[ClientEntryPoint]
[Alias("tasks.task")]
[Description("Durable task lifecycle neuron")]
public partial interface ITask : INeuron
{
    [Alias(nameof(Start))]
    Task<TaskSnapshot> Start(StartTask command);

    [Alias(nameof(Cancel))]
    Task<TaskSnapshot> Cancel(CancelTask command);

    [Alias(nameof(Read))]
    Task<TaskSnapshot> Read();
}
