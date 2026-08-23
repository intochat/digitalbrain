using DigitalBrain.Abstractions.Execution;

namespace DigitalBrain.Execution;

[GenerateSerializer]
[Alias("execution.state.v1")]
internal sealed record ExecutionState(
    [property: Id(0)] ExecutionId ExecutionId,
    [property: Id(1)] ExecutionStatus Status,
    [property: Id(2)] ExecutionDriverKind Driver,
    [property: Id(3)] WorkloadDescriptor Workload,
    [property: Id(4)] IReadOnlyList<CapabilityId> Grants);
