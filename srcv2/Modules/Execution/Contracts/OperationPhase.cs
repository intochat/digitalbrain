using DigitalBrain.Abstractions;

namespace DigitalBrain.Execution;

[GenerateSerializer]
[Alias("db.execution.operation-phase")]
public enum OperationPhase
{
    Prepared = 0,
    Dispatched = 1,
    Completed = 2,
    Uncertain = 3,
    Failed = 4,
}

