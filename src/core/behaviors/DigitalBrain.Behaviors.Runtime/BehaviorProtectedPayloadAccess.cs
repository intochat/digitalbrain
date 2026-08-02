using DigitalBrain.Abstractions;
using DigitalBrain.Tasks;

namespace DigitalBrain.Behaviors.Runtime;

internal interface IBehaviorProtectedPayloadAccess
{
    ValueTask<ProtectedPayloadReference> StoreAsync(
        OwnerId owner,
        NeuronId task,
        AttemptId attempt,
        ReadOnlyMemory<byte> plaintext,
        CancellationToken cancellationToken);

    ValueTask<ProtectedPayloadReference> StoreAsync(
        OwnerId owner,
        NeuronId task,
        AttemptId attempt,
        ReadOnlyMemory<byte> plaintext,
        TimeSpan lifetime,
        CancellationToken cancellationToken,
        Guid stableEntryId = default);

    ValueTask<ReadOnlyMemory<byte>> LoadAsync(
        OwnerId owner,
        NeuronId task,
        AttemptId attempt,
        ProtectedPayloadReference reference,
        CancellationToken cancellationToken);
}

internal sealed class GrainBehaviorProtectedPayloadAccess(IGrainFactory grains) : IBehaviorProtectedPayloadAccess
{
    private static readonly TimeSpan DefaultLifetime = TimeSpan.FromHours(1);

    public ValueTask<ProtectedPayloadReference> StoreAsync(
        OwnerId owner,
        NeuronId task,
        AttemptId attempt,
        ReadOnlyMemory<byte> plaintext,
        CancellationToken cancellationToken)
        => StoreAsync(owner, task, attempt, plaintext, DefaultLifetime, cancellationToken);

    public async ValueTask<ProtectedPayloadReference> StoreAsync(
        OwnerId owner,
        NeuronId task,
        AttemptId attempt,
        ReadOnlyMemory<byte> plaintext,
        TimeSpan lifetime,
        CancellationToken cancellationToken,
        Guid stableEntryId = default)
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

        if (lifetime <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(lifetime), lifetime, "Lifetime must be positive.");
        }

        var grain = grains.GetGrain<IBehaviorProtectedPayloadGrain>(owner.Value);
        return await grain
            .StoreAsync(task, attempt, plaintext.ToArray(), lifetime, cancellationToken, stableEntryId)
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
