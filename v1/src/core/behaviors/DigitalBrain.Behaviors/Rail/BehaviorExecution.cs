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
    DateTimeOffset UtcNow,
    NeuronId Worker = default,
    int HopsRemaining = BehaviorFactEmission.MaximumHops);

public sealed record LegacyBehaviorExecutionRequest(
    BehaviorExecutionMetadata Metadata,
    ReadOnlyMemory<byte> ArtifactBytes,
    string ArtifactHash,
    string TriggerTypeName,
    string TriggerJson,
    IBehaviorCapabilityResolver Capabilities,
    TimeProvider Time);

public sealed record BehaviorUserActionSurface(
    NeuronId Task,
    AttemptId Attempt,
    NeuronId Module,
    string ModuleId,
    string DisplayText,
    ProtectedPayloadReference ActionReference,
    Guid ActionEpoch,
    long ParkRevision,
    DateTimeOffset ExpiresAt,
    NeuronId Completer)
{
    public UserActionRequired ToRequirement()
        => new(
            Task,
            Attempt,
            Module,
            ModuleId,
            DisplayText,
            ActionReference,
            ActionEpoch,
            ParkRevision,
            ExpiresAt,
            Completer);

    public static BehaviorUserActionSurface FromRequirement(UserActionRequired requirement)
    {
        ArgumentNullException.ThrowIfNull(requirement);
        return new(
            requirement.Task,
            requirement.Attempt,
            requirement.Module,
            requirement.ModuleId,
            requirement.DisplayText,
            requirement.ActionReference,
            requirement.ActionEpoch,
            requirement.ParkRevision,
            requirement.ExpiresAt,
            requirement.Completer);
    }
}

public sealed record BehaviorExecutionOutcome(
    bool Succeeded,
    string Outcome,
    BehaviorUserActionSurface? UserAction = null);
