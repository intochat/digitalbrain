using DigitalBrain.Abstractions;
using DigitalBrain.Tasks;

namespace DigitalBrain.Behaviors;

internal interface IBehaviorProtectedTriggerAccess
{
    ValueTask<ProtectedPayloadReference> StoreAsync(
        OwnerId owner,
        NeuronId task,
        BehaviorId behavior,
        BehaviorRevisionId revision,
        string caseId,
        ReadOnlyMemory<byte> plaintext,
        CancellationToken cancellationToken);

    ValueTask<ReadOnlyMemory<byte>> LoadAsync(
        OwnerId owner,
        NeuronId task,
        BehaviorId behavior,
        BehaviorRevisionId revision,
        string caseId,
        ProtectedPayloadReference reference,
        CancellationToken cancellationToken);
}

internal sealed class GrainBehaviorProtectedTriggerAccess(IGrainFactory grains) : IBehaviorProtectedTriggerAccess
{
    public async ValueTask<ProtectedPayloadReference> StoreAsync(
        OwnerId owner,
        NeuronId task,
        BehaviorId behavior,
        BehaviorRevisionId revision,
        string caseId,
        ReadOnlyMemory<byte> plaintext,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ArgumentNullException.ThrowIfNull(grains);
        ArgumentException.ThrowIfNullOrWhiteSpace(caseId);

        if (owner == default)
        {
            throw new ArgumentException("Owner is required.", nameof(owner));
        }

        if (task.Owner != owner)
        {
            throw new InvalidOperationException("owner-task-mismatch");
        }

        var grain = grains.GetGrain<IBehaviorProtectedTriggerGrain>(owner.Value);
        return await grain
            .StoreAsync(task, behavior, revision, caseId, plaintext.ToArray(), cancellationToken)
            .ConfigureAwait(false);
    }

    public async ValueTask<ReadOnlyMemory<byte>> LoadAsync(
        OwnerId owner,
        NeuronId task,
        BehaviorId behavior,
        BehaviorRevisionId revision,
        string caseId,
        ProtectedPayloadReference reference,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ArgumentNullException.ThrowIfNull(grains);
        ArgumentException.ThrowIfNullOrWhiteSpace(caseId);

        if (owner == default)
        {
            throw new ArgumentException("Owner is required.", nameof(owner));
        }

        if (task.Owner != owner)
        {
            throw new InvalidOperationException("owner-task-mismatch");
        }

        var grain = grains.GetGrain<IBehaviorProtectedTriggerGrain>(owner.Value);
        var bytes = await grain
            .LoadAsync(task, behavior, revision, caseId, reference, cancellationToken)
            .ConfigureAwait(false);
        return bytes;
    }
}
