using DigitalBrain.Abstractions.Identity;

namespace DigitalBrain.Execution;

public sealed class ExecutionSeedBuilder
{
    public ExecutionSeedBuilder(
        ExecutionId executionId,
        OwnerId owner,
        WorkloadDescriptor workload,
        IReadOnlyList<ExecutionId> relatedExecutions)
    {
        ExecutionId = executionId;
        Owner = owner;
        Workload = workload;
        RelatedExecutions = relatedExecutions;
    }

    public ExecutionId ExecutionId { get; }

    public OwnerId Owner { get; }

    public WorkloadDescriptor Workload { get; }

    public IReadOnlyList<ExecutionId> RelatedExecutions { get; }

    public List<string> PromptBlocks { get; } = [];

    public List<ContextDelta> SeedDeltas { get; } = [];
}
