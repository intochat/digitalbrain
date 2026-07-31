using DigitalBrain.Abstractions;

namespace DigitalBrain.Security;

internal interface IProtectedTriggerStore
{
    ValueTask<ProtectedPayloadReference> StoreAsync(
        OwnerId owner,
        NeuronId task,
        BehaviorId behavior,
        BehaviorRevisionId revision,
        string caseId,
        ReadOnlyMemory<byte> plaintext,
        TimeSpan lifetime,
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
