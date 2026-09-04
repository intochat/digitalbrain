namespace DigitalBrain.Execution;

[GenerateSerializer]
[Alias("execution.state.v1")]
internal sealed record ExecutionState(
    [property: Id(0)] ExecutionId ExecutionId,
    [property: Id(1)] ExecutionStatus Status,
    [property: Id(2)] WorkloadDescriptor Workload,
    [property: Id(3)] IReadOnlyList<string>? PromptBlocks = null);
