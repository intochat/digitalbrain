using DigitalBrain.Kernel;
using DigitalBrain.Tasks;
using Microsoft.Extensions.AI;

namespace DigitalBrain.AI;

[GenerateSerializer]
[Alias("ai.workflow-checkpoint-reference")]
internal sealed record WorkflowCheckpointReference(
    [property: Id(0)] string SessionId,
    [property: Id(1)] string CheckpointId);

[GenerateSerializer]
[Alias("ai.workflow-run")]
internal sealed record WorkflowRun(
    [property: Id(0)] Guid RunId,
    [property: Id(1)] AttemptCursor Cursor,
    [property: Id(2)] string DefinitionFingerprint,
    [property: Id(3)] WorkflowCheckpointReference? InputCheckpoint,
    [property: Id(4)] DateTimeOffset RecoverAfterUtc);

[GenerateSerializer]
[Alias("ai.workflow-run-command")]
internal sealed record WorkflowRunCommand(
    [property: Id(0)] WorkflowRun Run,
    [property: Id(1)] OrchestrationDefinition Definition,
    [property: Id(2)] ChatMessage[] ReplayInput,
    [property: Id(3)] CapabilityDelegation Completion);

[GenerateSerializer]
[Alias("ai.workflow-run-result")]
internal sealed record WorkflowRunResult(
    [property: Id(0)] WorkflowRun Run,
    [property: Id(1)] WorkflowCheckpointReference OutputCheckpoint,
    [property: Id(2)] ChatMessage[]? TerminalMessages);
