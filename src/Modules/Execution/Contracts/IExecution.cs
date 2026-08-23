using DigitalBrain.Abstractions.Neurons;

namespace DigitalBrain.Execution;

[Alias("db.execution")]
public partial interface IExecution :
    INeuron,
    IHandle<StartExecution>,
    IHandle<CancelExecution>
{
    [Alias(nameof(Read))]
    Task<ExecutionProjection> Read();
}
