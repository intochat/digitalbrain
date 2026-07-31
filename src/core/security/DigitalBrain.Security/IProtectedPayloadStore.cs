using DigitalBrain.Abstractions;

namespace DigitalBrain.Security;

internal interface IProtectedPayloadStore
{
    ValueTask<ProtectedPayloadReference> StoreAsync(
        OwnerId owner,
        NeuronId task,
        Guid attempt,
        ReadOnlyMemory<byte> plaintext,
        TimeSpan lifetime,
        CancellationToken cancellationToken);

    ValueTask<ReadOnlyMemory<byte>> LoadAsync(
        OwnerId owner,
        NeuronId task,
        Guid attempt,
        ProtectedPayloadReference reference,
        CancellationToken cancellationToken);
}
