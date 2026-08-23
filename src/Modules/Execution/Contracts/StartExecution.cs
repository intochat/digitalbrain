using DigitalBrain.Abstractions.Execution;
using DigitalBrain.Abstractions.Identity;
using DigitalBrain.Abstractions.Messaging;

namespace DigitalBrain.Execution;

[GenerateSerializer]
[Alias("db.execution.start.v1")]
public sealed record StartExecution(
    [property: Id(0)] CommandId CommandId,
    [property: Id(1)] ExecutionId ExecutionId,
    [property: Id(2)] WorkloadDescriptor Workload,
    [property: Id(3)] ExecutionDriverKind Driver,
    [property: Id(4)] IReadOnlyList<CapabilityId> Grants,
    [property: Id(5)] IReadOnlyList<ExecutionId>? RelatedExecutions = null) : Synapse;
