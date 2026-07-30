using DigitalBrain.Abstractions;
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

        var activation = new BehaviorTaskActivation(
            binding.BehaviorId,
            binding.Revision,
            binding.ContractVersion,
            binding.CaseId,
            binding.ProtectedPayload);
        var goal = new BehaviorActivationGoal(
            binding.BehaviorId,
            binding.Revision,
            binding.ContractVersion,
            binding.CaseId,
            binding.ProtectedPayload);
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
}
