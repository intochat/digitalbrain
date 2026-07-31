namespace DigitalBrain.Behaviors;

using DigitalBrain.Abstractions;
using DigitalBrain.Tasks;

public sealed record BehaviorHostDeployCommand(
    OwnerId Owner,
    BehaviorId Behavior,
    string ArtifactHash,
    ReadOnlyMemory<byte> ArtifactBytes,
    ReadOnlyMemory<byte> AssemblyBytes,
    ReadOnlyMemory<byte> Signature);

public sealed record BehaviorHostActivationCommand(
    OwnerId Owner,
    BehaviorId Behavior,
    string ArtifactHash);

public sealed record BehaviorHostDeactivationCommand(
    OwnerId Owner,
    BehaviorId Behavior,
    string ArtifactHash);

public sealed record BehaviorHostExecuteCommand(
    BehaviorExecutionMetadata Metadata,
    string ArtifactHash,
    NeuronId Task,
    AttemptId Attempt,
    string TriggerTypeName,
    ProtectedPayloadReference TriggerPayload,
    IReadOnlyList<BehaviorCapabilityEdge> Capabilities,
    DateTimeOffset UtcNow);
