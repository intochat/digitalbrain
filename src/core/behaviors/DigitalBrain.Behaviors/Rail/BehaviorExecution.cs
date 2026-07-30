namespace DigitalBrain.Behaviors;

public sealed record BehaviorExecutionRequest(
    BehaviorExecutionMetadata Metadata,
    ReadOnlyMemory<byte> ArtifactBytes,
    string ArtifactHash,
    string TriggerTypeName,
    string TriggerJson,
    IBehaviorCapabilityResolver Capabilities,
    TimeProvider Time);

public sealed record BehaviorExecutionOutcome(bool Succeeded, string Outcome);
