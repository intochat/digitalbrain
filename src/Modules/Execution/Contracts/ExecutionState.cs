namespace DigitalBrain.Execution;

[GenerateSerializer]
[Alias("db.execution.state")]
public enum ExecutionState
{
    Pending,
    Running,
    Waiting,
    Cancelling,
    Succeeded,
    Failed,
    Cancelled,
}
