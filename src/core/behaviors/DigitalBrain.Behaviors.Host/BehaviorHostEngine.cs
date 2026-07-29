using System.Collections.Concurrent;
using DigitalBrain.Abstractions;
using DigitalBrain.Behaviors.Artifacts;

namespace DigitalBrain.Behaviors;

public sealed class BehaviorHostEngine(IBehaviorArtifactTrust trust) : IBehaviorHostGateway
{
    private readonly ConcurrentDictionary<string, DeployedRevision> _deployed = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, string> _active = new(StringComparer.Ordinal);

    public ValueTask DeployAsync(BehaviorHostDeployCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        cancellationToken.ThrowIfCancellationRequested();
        ArgumentException.ThrowIfNullOrWhiteSpace(command.ArtifactHash);

        if (command.ArtifactBytes.IsEmpty || command.AssemblyBytes.IsEmpty)
        {
            throw new BehaviorHostException("missing-artifact-bytes");
        }

        BehaviorHostTestFaults.ThrowIfArmed();

        var computed = BehaviorArtifactDigest.Compute(command.ArtifactBytes.Span);
        if (!string.Equals(computed.Value, command.ArtifactHash, StringComparison.Ordinal))
        {
            throw new BehaviorHostException("artifact-hash-mismatch");
        }

        trust.Verify(command.ArtifactHash, command.Signature.Span);

        var key = RevisionKey(command.Owner, command.Behavior, command.ArtifactHash);
        _deployed[key] = new DeployedRevision(
            command.Owner,
            command.Behavior,
            command.ArtifactHash,
            command.ArtifactBytes.ToArray(),
            command.AssemblyBytes.ToArray(),
            command.Signature.ToArray());

        return ValueTask.CompletedTask;
    }

    public ValueTask ActivateAsync(BehaviorHostActivationCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        cancellationToken.ThrowIfCancellationRequested();
        ArgumentException.ThrowIfNullOrWhiteSpace(command.ArtifactHash);

        var key = RevisionKey(command.Owner, command.Behavior, command.ArtifactHash);
        if (!_deployed.ContainsKey(key))
        {
            throw new BehaviorHostException("revision-not-deployed");
        }

        _active[BehaviorKey(command.Owner, command.Behavior)] = command.ArtifactHash;
        return ValueTask.CompletedTask;
    }

    public ValueTask DeactivateAsync(BehaviorHostDeactivationCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        cancellationToken.ThrowIfCancellationRequested();
        ArgumentException.ThrowIfNullOrWhiteSpace(command.ArtifactHash);

        var behaviorKey = BehaviorKey(command.Owner, command.Behavior);
        if (_active.TryGetValue(behaviorKey, out var active)
            && string.Equals(active, command.ArtifactHash, StringComparison.Ordinal))
        {
            _active.TryRemove(behaviorKey, out _);
        }

        return ValueTask.CompletedTask;
    }

    public async ValueTask<BehaviorExecutionOutcome> ExecuteAsync(
        BehaviorHostExecuteCommand command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        cancellationToken.ThrowIfCancellationRequested();
        ArgumentException.ThrowIfNullOrWhiteSpace(command.ArtifactHash);
        ArgumentNullException.ThrowIfNull(command.Capabilities);
        ArgumentNullException.ThrowIfNull(command.Time);

        var behaviorKey = BehaviorKey(command.Metadata.Owner, command.Metadata.Behavior);
        if (!_active.TryGetValue(behaviorKey, out var activeHash)
            || !string.Equals(activeHash, command.ArtifactHash, StringComparison.Ordinal))
        {
            throw new BehaviorHostException("revision-not-active");
        }

        var revisionKey = RevisionKey(command.Metadata.Owner, command.Metadata.Behavior, command.ArtifactHash);
        if (!_deployed.TryGetValue(revisionKey, out var revision))
        {
            throw new BehaviorHostException("revision-not-deployed");
        }

        trust.Verify(revision.ArtifactHash, revision.Signature);

        return await BehaviorProgramLoader.ExecuteAsync(
            new BehaviorExecutionRequest(
                command.Metadata,
                revision.AssemblyBytes,
                revision.ArtifactHash,
                command.TriggerTypeName,
                command.TriggerJson,
                command.Capabilities,
                command.Time),
            cancellationToken).ConfigureAwait(false);
    }

    private static string BehaviorKey(OwnerId owner, BehaviorId behavior)
        => $"{owner.Value}\u001f{behavior.Value}";

    private static string RevisionKey(OwnerId owner, BehaviorId behavior, string artifactHash)
        => $"{BehaviorKey(owner, behavior)}\u001f{artifactHash}";

    private sealed record DeployedRevision(
        OwnerId Owner,
        BehaviorId Behavior,
        string ArtifactHash,
        byte[] ArtifactBytes,
        byte[] AssemblyBytes,
        byte[] Signature);
}
