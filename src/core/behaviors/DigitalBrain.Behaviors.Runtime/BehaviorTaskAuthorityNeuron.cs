using DigitalBrain.Abstractions;
using DigitalBrain.Kernel;
using DigitalBrain.Tasks;

namespace DigitalBrain.Behaviors;

[GrainType(BehaviorTaskAuthority.GrainTypeName)]
internal sealed class BehaviorTaskAuthorityNeuron : Neuron, IBehaviorTaskAuthority
{
    public async Task<TaskSnapshot> ReadValidatedTask(
        NeuronId task,
        AttemptId attempt,
        bool requireActivation,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (task == default || string.IsNullOrWhiteSpace(task.Type) || string.IsNullOrWhiteSpace(task.Name))
        {
            throw new ArgumentException("missing-task-identity", paramName: null);
        }

        if (task.Owner != Id.Owner)
        {
            throw new InvalidOperationException("owner-task-mismatch");
        }

        if (!string.Equals(
                task.Type,
                NeuronId.GrainTypeNameOf(typeof(ITask)),
                StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("invalid-task-identity");
        }

        if (attempt == default || attempt.Value == Guid.Empty)
        {
            throw new ArgumentException("invalid-attempt", paramName: null);
        }

        cancellationToken.ThrowIfCancellationRequested();

        TaskSnapshot snapshot;
        try
        {
            snapshot = await GrainFactory.GetGrain<ITask>(task.ToGrainId()).Read();
        }
        catch (InvalidOperationException exception) when (
            exception.Message.Contains("has not been started", StringComparison.Ordinal))
        {
            throw new InvalidOperationException("task-not-started");
        }

        cancellationToken.ThrowIfCancellationRequested();

        if (snapshot.Worker == default
            || !string.Equals(
                snapshot.Worker.Type,
                NeuronId.GrainTypeNameOf(typeof(IWorker)),
                StringComparison.OrdinalIgnoreCase)
            || snapshot.Worker.Owner != Id.Owner)
        {
            throw new InvalidOperationException("worker-mismatch");
        }

        if (snapshot.ActiveAttempt is null || snapshot.ActiveAttempt != attempt)
        {
            throw new InvalidOperationException("attempt-mismatch");
        }

        if (requireActivation && snapshot.Activation is null)
        {
            throw new InvalidOperationException("activation-required");
        }

        return snapshot;
    }
}
