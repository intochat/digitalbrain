namespace DigitalBrain.Execution;

[GenerateSerializer]
[Alias("db.execution.status")]
public enum ExecutionStatus : byte
{
    Pending = 0,
    Running = 1,
    AwaitingApproval = 2,
    Completed = 3,
    Failed = 4,
    Cancelled = 5,
    Uncertain = 6,
}
