using DigitalBrain.Abstractions;
using DigitalBrain.Behaviors.Manifest;
using DigitalBrain.Behaviors.Runtime.Artifacts;
using DigitalBrain.Tasks;

namespace DigitalBrain.Behaviors;

internal sealed partial class BehaviorNeuron
{
    public async Task<BoundBehaviorActivationResult> ActivateBound(ActivateBoundBehavior command)
    {
        ArgumentNullException.ThrowIfNull(command);
        ArgumentNullException.ThrowIfNull(command.Binding);
        ValidateCommand(command.CommandId);
        ArgumentException.ThrowIfNullOrWhiteSpace(command.ArtifactHash);
        ArgumentException.ThrowIfNullOrWhiteSpace(command.Binding.ContractVersion);
        ArgumentException.ThrowIfNullOrWhiteSpace(command.Binding.CaseId);

        var data = LoadOrEmpty();
        var binding = command.Binding;
        var behaviorId = BehaviorIdOfName();

        if (!string.Equals(data.ActiveArtifactHash, command.ArtifactHash, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                $"Behavior '{Id}' has no active revision '{command.ArtifactHash}' to bind.");
        }

        if (binding.BehaviorId != behaviorId
            || !string.Equals(binding.Revision.Value, command.ArtifactHash, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                $"Behavior binding does not identify active behavior '{behaviorId}' at '{command.ArtifactHash}'.");
        }

        if (binding.TaskId.Owner != Id.Owner
            || binding.WorkerId.Owner != Id.Owner
            || !string.Equals(
                binding.TaskId.Type,
                NeuronId.GrainTypeNameOf(typeof(ITask)),
                StringComparison.OrdinalIgnoreCase)
            || !string.Equals(
                binding.WorkerId.Type,
                NeuronId.GrainTypeNameOf(typeof(IWorker)),
                StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                "Behavior activation requires an existing owner-scoped Task and Worker.");
        }

        if (data.ActiveArtifactBytes is null)
        {
            throw new InvalidOperationException(
                $"Behavior '{Id}' has no signed active artifact bytes for '{command.ArtifactHash}'.");
        }

        var envelope = CanonicalArtifactReader.Read(data.ActiveArtifactBytes);
        var contract = envelope.Manifest.EntryPoints.Contract;
        if (contract.ContractMajorVersion.ToString(System.Globalization.CultureInfo.InvariantCulture)
            != binding.ContractVersion)
        {
            throw new InvalidOperationException(
                $"Behavior binding contract version '{binding.ContractVersion}' does not match signed contract '{contract.ContractMajorVersion}'.");
        }

        var signedCase = contract.Cases.FirstOrDefault(
            item => string.Equals(item.CaseId, binding.CaseId, StringComparison.Ordinal));
        if (signedCase is null)
        {
            throw new InvalidOperationException(
                $"Behavior binding case '{binding.CaseId}' is not present on the active signed contract.");
        }

        var capabilities = DeriveResultBearingEdges(Id.Owner, envelope.Manifest.CapabilityGrants);
        var activation = new BehaviorTaskActivation(
            binding.BehaviorId,
            binding.Revision,
            binding.ContractVersion,
            binding.CaseId,
            binding.ProtectedPayload,
            signedCase.CaseName,
            capabilities);
        var goal = new BehaviorActivationGoal(
            binding.BehaviorId,
            binding.Revision,
            binding.ContractVersion,
            binding.CaseId,
            binding.ProtectedPayload,
            signedCase.CaseName,
            capabilities);
        var snapshot = await GrainFactory
            .GetGrain<ITask>(binding.TaskId.ToGrainId())
            .Start(new StartTask(
                command.CommandId,
                goal,
                binding.WorkerId,
                new TaskPolicy(1, TimeSpan.Zero, null),
                Activation: activation));

        if (snapshot.Worker != binding.WorkerId || snapshot.Activation != activation)
        {
            throw new InvalidOperationException(
                $"Task '{binding.TaskId}' is already bound to a different activation.");
        }

        return new BoundBehaviorActivationResult(
            binding.TaskId,
            snapshot.State,
            snapshot.ActiveAttempt,
            snapshot.Activation);
    }

    private static TaskOperationEdge[] DeriveResultBearingEdges(
        OwnerId owner,
        IReadOnlyList<BehaviorCapabilityGrant> grants)
    {
        ArgumentNullException.ThrowIfNull(grants);

        if (grants.Count == 0)
        {
            return [];
        }

        var edges = new TaskOperationEdge[grants.Count];
        for (var index = 0; index < grants.Count; index++)
        {
            var grant = grants[index];
            if (string.IsNullOrWhiteSpace(grant.EmittedResultSynapseId)
                || grant.EmittedResultSchemaVersion is null)
            {
                throw new InvalidOperationException("one-way-capability-not-supported");
            }

            edges[index] = new TaskOperationEdge(
                new NeuronId(grant.TargetNeuronContractId, owner, grant.TargetInstanceName),
                grant.AcceptedRequestSynapseId,
                grant.AcceptedRequestSchemaVersion,
                grant.EmittedResultSynapseId,
                grant.EmittedResultSchemaVersion.Value);
        }

        return edges;
    }
}
