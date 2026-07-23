using DigitalBrain.Abstractions;
using DigitalBrain.Tasks;
using Microsoft.Extensions.AI;

namespace DigitalBrain.AI;

[GenerateSerializer]
[Alias("ai.worker-state")]
internal sealed record AIWorkerState(
    [property: Id(0)] AttemptCursor Cursor,
    [property: Id(1)] ChatMessage[] ReplayInput,
    [property: Id(2)] OrchestrationDefinition Definition,
    [property: Id(3)] WorkflowCheckpointReference? Checkpoint,
    [property: Id(4)] SynapseId Causation,
    [property: Id(5)] WorkflowRun? ActiveRun,
    [property: Id(6)] SupervisedAttemptLifecycle Lifecycle);

internal enum SupervisedAttemptLifecycle
{
    Unknown,
    Running,
    AwaitingContinuation,
    Succeeded,
    Cancelled,
}

internal static class SupervisedAttemptLifecycleRules
{
    internal static bool CanContinue(this SupervisedAttemptLifecycle lifecycle)
        => lifecycle == SupervisedAttemptLifecycle.AwaitingContinuation;

    internal static bool AllowsDirect(this SupervisedAttemptLifecycle lifecycle)
        => lifecycle is SupervisedAttemptLifecycle.Succeeded
            or SupervisedAttemptLifecycle.Cancelled;
}
