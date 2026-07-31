using DigitalBrain.Abstractions;
using DigitalBrain.Tasks;

namespace DigitalBrain.Behaviors;

internal interface IBehaviorProtectedPayloadAccess
{
    ValueTask<ProtectedPayloadReference> StoreAsync(
        OwnerId owner,
        NeuronId task,
        AttemptId attempt,
        ReadOnlyMemory<byte> plaintext,
        CancellationToken cancellationToken);

    ValueTask<ReadOnlyMemory<byte>> LoadAsync(
        OwnerId owner,
        NeuronId task,
        AttemptId attempt,
        ProtectedPayloadReference reference,
        CancellationToken cancellationToken);
}

internal sealed class GrainBehaviorProtectedPayloadAccess(IGrainFactory grains) : IBehaviorProtectedPayloadAccess
{
    public async ValueTask<ProtectedPayloadReference> StoreAsync(
        OwnerId owner,
        NeuronId task,
        AttemptId attempt,
        ReadOnlyMemory<byte> plaintext,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ArgumentNullException.ThrowIfNull(grains);

        if (owner == default)
        {
            throw new ArgumentException("Owner is required.", nameof(owner));
        }

        if (task.Owner != owner)
        {
            throw new InvalidOperationException("owner-task-mismatch");
        }

        var grain = grains.GetGrain<IBehaviorProtectedPayloadGrain>(owner.Value);
        return await grain
            .StoreAsync(task, attempt, plaintext.ToArray(), cancellationToken)
            .ConfigureAwait(false);
    }

    public async ValueTask<ReadOnlyMemory<byte>> LoadAsync(
        OwnerId owner,
        NeuronId task,
        AttemptId attempt,
        ProtectedPayloadReference reference,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ArgumentNullException.ThrowIfNull(grains);

        if (owner == default)
        {
            throw new ArgumentException("Owner is required.", nameof(owner));
        }

        if (task.Owner != owner)
        {
            throw new InvalidOperationException("owner-task-mismatch");
        }

        var grain = grains.GetGrain<IBehaviorProtectedPayloadGrain>(owner.Value);
        var bytes = await grain
            .LoadAsync(task, attempt, reference, cancellationToken)
            .ConfigureAwait(false);
        return bytes;
    }
}
