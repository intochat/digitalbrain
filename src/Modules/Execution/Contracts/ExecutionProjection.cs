
namespace DigitalBrain.Execution;

[GenerateSerializer]
[Alias("db.execution-projection.v1")]
public sealed record ExecutionProjection(
    [property: Id(0)] ExecutionId ExecutionId,
    [property: Id(1)] ExecutionStatus Status,
    [property: Id(2)] ExecutionDriverKind Driver,
    [property: Id(3)] WorkloadDescriptor Workload,
    [property: Id(4)] IReadOnlyList<string>? PromptBlocks = null);
