using System.Collections.Concurrent;
using DigitalBrain.Abstractions;
using DigitalBrain.Behaviors.Artifacts;
using DigitalBrain.Behaviors.Manifest;
using DigitalBrain.Tasks;

namespace DigitalBrain.Behaviors.Host;

public sealed class BehaviorHostEngine : IBehaviorHostGateway
{
    private readonly IBehaviorArtifactTrust trust;
    private readonly IBehaviorHostBrokerClientFactory? brokerFactory;
    private readonly ConcurrentDictionary<string, DeployedRevision> _deployed = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, string> _active = new(StringComparer.Ordinal);

    public BehaviorHostEngine(IBehaviorArtifactTrust trust)
        : this(trust, brokerFactory: null)
    {
    }

    internal BehaviorHostEngine(
        IBehaviorArtifactTrust trust,
        IBehaviorHostBrokerClientFactory? brokerFactory)
    {
        ArgumentNullException.ThrowIfNull(trust);
        this.trust = trust;
        this.brokerFactory = brokerFactory;
    }

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

        var envelope = CanonicalArtifactReader.Read(command.ArtifactBytes);
        if (!envelope.BehaviorAssembly.Span.SequenceEqual(command.AssemblyBytes.Span))
        {
            throw new BehaviorHostException("embedded-assembly-mismatch");
        }

        var key = RevisionKey(command.Owner, command.Behavior, command.ArtifactHash);
        _deployed[key] = new DeployedRevision(
            command.Owner,
            command.Behavior,
            command.ArtifactHash,
            command.ArtifactBytes.ToArray(),
            envelope.BehaviorAssembly.ToArray(),
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

        var revision = EnsureActiveDeployedRevision(
            command.Metadata.Owner,
            command.Metadata.Behavior,
            command.ArtifactHash);

        if (command.Metadata.Behavior != revision.Behavior)
        {
            throw new BehaviorHostException("behavior-mismatch");
        }

        if (command.Metadata.Owner != command.Task.Owner)
        {
            throw new BehaviorHostException("owner-task-mismatch");
        }

        var taskGrainType = NeuronId.GrainTypeNameOf(typeof(ITask));
        if (!string.Equals(command.Task.Type, taskGrainType, StringComparison.OrdinalIgnoreCase))
        {
            throw new BehaviorHostException("task-not-itask");
        }

        if (!string.Equals(command.Metadata.Revision.Value, command.ArtifactHash, StringComparison.Ordinal))
        {
            throw new BehaviorHostException("revision-hash-mismatch");
        }

        var envelope = CanonicalArtifactReader.Read(revision.ArtifactBytes);
        if (envelope.Manifest.Behavior != command.Metadata.Behavior)
        {
            throw new BehaviorHostException("manifest-behavior-mismatch");
        }

        var signedEdges = DeriveResultBearingEdges(command.Metadata.Owner, envelope.Manifest.CapabilityGrants);
        if (!CapabilityMultisetsEqual(command.Capabilities, signedEdges))
        {
            throw new BehaviorHostException("capability-grant-mismatch");
        }

        if (brokerFactory is null)
        {
            throw new BehaviorHostException(BehaviorExecutionCodes.HostNotConfigured);
        }

        if (command.Worker == default
            || command.Worker.Owner != command.Metadata.Owner
            || !string.Equals(
                command.Worker.Type,
                NeuronId.GrainTypeNameOf(typeof(IWorker)),
                StringComparison.OrdinalIgnoreCase))
        {
            throw new BehaviorHostException(BehaviorExecutionCodes.TriggerUnauthorized);
        }

        cancellationToken.ThrowIfCancellationRequested();
        var client = brokerFactory.Create(
            command.Metadata.Owner,
            command.Task,
            command.Attempt,
            command.Worker);
        var triggerCase = ResolveTriggerCase(envelope.Manifest.EntryPoints.Contract, command.TriggerTypeName);
        ReadOnlyMemory<byte> triggerBytes;
        try
        {
            triggerBytes = await client.LoadTriggerAsync(
                command.Metadata.Owner,
                command.Task,
                command.Metadata.Behavior,
                command.Metadata.Revision,
                triggerCase.CaseId,
                command.TriggerPayload,
                cancellationToken).ConfigureAwait(false);
        }
        catch (BehaviorHostException exception)
        {
            throw new BehaviorHostException(
                BehaviorExecutionCodes.MapHostFailure(exception.Reason),
                exception);
        }

        cancellationToken.ThrowIfCancellationRequested();
        var broker = new HostBehaviorSynapseBroker(
            command.Metadata,
            command.Task,
            command.Attempt,
            signedEdges,
            client,
            command.HopsRemaining);

        return await BehaviorProgramLoader.ExecuteAsync(
            new BehaviorExecutionRequest(
                command.Metadata,
                envelope.BehaviorAssembly,
                command.ArtifactHash,
                command.Task,
                command.Attempt,
                command.TriggerTypeName,
                command.TriggerPayload,
                signedEdges,
                command.UtcNow,
                command.Worker,
                command.HopsRemaining),
            triggerBytes,
            broker,
            cancellationToken).ConfigureAwait(false);
    }

    public async ValueTask<BehaviorExecutionOutcome> ExecuteLegacyAsync(
        LegacyBehaviorExecutionRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();
        ArgumentException.ThrowIfNullOrWhiteSpace(request.ArtifactHash);
        ArgumentNullException.ThrowIfNull(request.Capabilities);
        ArgumentNullException.ThrowIfNull(request.Time);

        var revision = EnsureActiveDeployedRevision(
            request.Metadata.Owner,
            request.Metadata.Behavior,
            request.ArtifactHash);

        return await BehaviorProgramLoader.ExecuteAsync(
            new LegacyBehaviorExecutionRequest(
                request.Metadata,
                revision.AssemblyBytes,
                revision.ArtifactHash,
                request.TriggerTypeName,
                request.TriggerJson,
                request.Capabilities,
                request.Time),
            cancellationToken).ConfigureAwait(false);
    }

    private DeployedRevision EnsureActiveDeployedRevision(OwnerId owner, BehaviorId behavior, string artifactHash)
    {
        var behaviorKey = BehaviorKey(owner, behavior);
        if (!_active.TryGetValue(behaviorKey, out var activeHash)
            || !string.Equals(activeHash, artifactHash, StringComparison.Ordinal))
        {
            throw new BehaviorHostException("revision-not-active");
        }

        var revisionKey = RevisionKey(owner, behavior, artifactHash);
        if (!_deployed.TryGetValue(revisionKey, out var revision))
        {
            throw new BehaviorHostException("revision-not-deployed");
        }

        trust.Verify(revision.ArtifactHash, revision.Signature);
        return revision;
    }

    private static BehaviorContractCaseManifest ResolveTriggerCase(
        BehaviorContractManifest contract,
        string triggerTypeName)
    {
        ArgumentNullException.ThrowIfNull(contract);
        ArgumentException.ThrowIfNullOrWhiteSpace(triggerTypeName);

        BehaviorContractCaseManifest? selected = null;
        foreach (var item in contract.Cases)
        {
            if (!string.Equals(item.CaseName, triggerTypeName, StringComparison.Ordinal)
                && !string.Equals(item.CaseName, triggerTypeName, StringComparison.OrdinalIgnoreCase)
                && !triggerTypeName.EndsWith("." + item.CaseName, StringComparison.Ordinal))
            {
                continue;
            }

            if (selected is not null)
            {
                throw new BehaviorHostException("ambiguous-trigger-case");
            }

            selected = item;
        }

        return selected ?? throw new BehaviorHostException("unknown-trigger-case");
    }

    private static BehaviorCapabilityEdge[] DeriveResultBearingEdges(
        OwnerId owner,
        IReadOnlyList<BehaviorCapabilityGrant> grants)
    {
        ArgumentNullException.ThrowIfNull(grants);

        var edges = new BehaviorCapabilityEdge[grants.Count];
        for (var index = 0; index < grants.Count; index++)
        {
            var grant = grants[index];
            if (string.IsNullOrWhiteSpace(grant.EmittedResultSynapseId)
                || grant.EmittedResultSchemaVersion is null)
            {
                throw new BehaviorHostException("one-way-capability-not-supported");
            }

            edges[index] = new BehaviorCapabilityEdge(
                new NeuronId(grant.TargetNeuronContractId, owner, grant.TargetInstanceName),
                grant.AcceptedRequestSynapseId,
                grant.AcceptedRequestSchemaVersion,
                grant.EmittedResultSynapseId,
                grant.EmittedResultSchemaVersion.Value);
        }

        return edges;
    }

    private static bool CapabilityMultisetsEqual(
        IReadOnlyList<BehaviorCapabilityEdge> left,
        BehaviorCapabilityEdge[] right)
    {
        if (left.Count != right.Length)
        {
            return false;
        }

        var remaining = new List<BehaviorCapabilityEdge>(right);
        foreach (var edge in left)
        {
            var match = remaining.FindIndex(candidate => EdgesEqual(edge, candidate));
            if (match < 0)
            {
                return false;
            }

            remaining.RemoveAt(match);
        }

        return remaining.Count == 0;
    }

    private static bool EdgesEqual(BehaviorCapabilityEdge left, BehaviorCapabilityEdge right)
        => left.Target == right.Target
            && string.Equals(left.RequestSynapseId, right.RequestSynapseId, StringComparison.Ordinal)
            && left.RequestSchemaVersion == right.RequestSchemaVersion
            && string.Equals(left.ResponseSynapseId, right.ResponseSynapseId, StringComparison.Ordinal)
            && left.ResponseSchemaVersion == right.ResponseSchemaVersion;

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
