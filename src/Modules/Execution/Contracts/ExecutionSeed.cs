using DigitalBrain.Abstractions.Execution;

namespace DigitalBrain.Execution;

[GenerateSerializer, Alias("db.execution-seed.v1")]
public sealed record ExecutionSeed(
    [property: Id(0)] ExecutionId ExecutionId,
    [property: Id(1)] WorkloadDescriptor Workload,
    [property: Id(2)] IReadOnlyList<string> PromptBlocks,
    [property: Id(3)] IReadOnlyList<ContextDelta> SeedDeltas);
