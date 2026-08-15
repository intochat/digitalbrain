using DigitalBrain.Abstractions;

namespace DigitalBrain.Execution;

[GenerateSerializer]
[Alias("db.execution.operation-resolution")]
public enum OperationResolution
{
    Completed = 0,
    Failed = 1,
    PermitRetry = 2,
}

