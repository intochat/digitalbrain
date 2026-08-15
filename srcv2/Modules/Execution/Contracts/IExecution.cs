using DigitalBrain.Abstractions;

namespace DigitalBrain.Execution;

[ClientEntryPoint]
[Alias("db.execution")]
public partial interface IExecution :
    INeuron,
    IHandle<ApplyExecution>
{
    [Alias(nameof(Apply))]
    Task<ExecutionSnapshot> Apply(ApplyExecution command);

    [Alias(nameof(Read))]
    Task<ExecutionSnapshot> Read();
}
