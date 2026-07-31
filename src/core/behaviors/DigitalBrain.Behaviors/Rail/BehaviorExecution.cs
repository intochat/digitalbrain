namespace DigitalBrain.Behaviors;

using DigitalBrain.Abstractions;
using DigitalBrain.Tasks;

public sealed record BehaviorExecutionRequest(
    BehaviorExecutionMetadata Metadata,
    ReadOnlyMemory<byte> ArtifactBytes,
    string ArtifactHash,
    NeuronId Task,
    AttemptId Attempt,
    string TriggerTypeName,
    ProtectedPayloadReference TriggerPayload,
    IReadOnlyList<BehaviorCapabilityEdge> Capabilities,
    DateTimeOffset UtcNow);

public sealed record LegacyBehaviorExecutionRequest(
    BehaviorExecutionMetadata Metadata,
    ReadOnlyMemory<byte> ArtifactBytes,
    string ArtifactHash,
    string TriggerTypeName,
    string TriggerJson,
    IBehaviorCapabilityResolver Capabilities,
    TimeProvider Time);

public sealed record BehaviorExecutionOutcome(bool Succeeded, string Outcome);
