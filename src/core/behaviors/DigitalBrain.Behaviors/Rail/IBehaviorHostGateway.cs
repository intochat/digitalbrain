namespace DigitalBrain.Behaviors;

using DigitalBrain.Abstractions;

public interface IBehaviorHostGateway
{
    ValueTask DeployAsync(BehaviorHostDeployCommand command, CancellationToken cancellationToken);

    ValueTask ActivateAsync(BehaviorHostActivationCommand command, CancellationToken cancellationToken);

    ValueTask DeactivateAsync(BehaviorHostDeactivationCommand command, CancellationToken cancellationToken);

    ValueTask<BehaviorExecutionOutcome> ExecuteAsync(
        BehaviorHostExecuteCommand command,
        CancellationToken cancellationToken);
}

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
    string TriggerTypeName,
    string TriggerJson,
    IBehaviorCapabilityResolver Capabilities,
    TimeProvider Time);

public interface IBehaviorArtifactTrust
{
    byte[] Sign(string artifactHash);

    void Verify(string artifactHash, ReadOnlySpan<byte> signature);
}

public sealed class BehaviorHostException : Exception
{
    public BehaviorHostException()
        : this("behavior-host-error")
    {
    }

    public BehaviorHostException(string reason)
        : base(reason)
    {
        Reason = reason;
    }

    public BehaviorHostException(string reason, Exception innerException)
        : base(reason, innerException)
    {
        Reason = reason;
    }

    public string Reason { get; } = "behavior-host-error";
}
