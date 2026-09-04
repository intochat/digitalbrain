using DigitalBrain.Abstractions.Signals;

namespace DigitalBrain.Execution;

[GenerateSerializer]
[Alias("db.execution-projection.v1")]
public sealed record ExecutionProjection(
    [property: Id(0)] ExecutionId ExecutionId,
    [property: Id(1)] ExecutionStatus Status,
    [property: Id(2)] WorkloadDescriptor Workload,
    [property: Id(3)] IReadOnlyList<string>? PromptBlocks = null) : Signal;
