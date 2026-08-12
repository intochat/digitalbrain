using DigitalBrain.Abstractions;

namespace DigitalBrain.Execution;

[GenerateSerializer]
[Alias("db.execution.apply")]
public sealed record ApplyExecution(
    [property: Id(0)] CommandId CommandId,
    [property: Id(1)] ExecutionApplyCommand Command,
    [property: Id(2)] long? ExpectedRevision = null) : RequestSynapse<ExecutionSnapshot>;

