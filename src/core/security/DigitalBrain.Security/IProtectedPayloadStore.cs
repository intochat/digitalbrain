using DigitalBrain.Abstractions;

namespace DigitalBrain.Security;

internal interface IProtectedPayloadStore
{
    ValueTask<ProtectedPayloadReference> StoreAsync(
        OwnerId owner,
        ReadOnlyMemory<byte> plaintext,
        TimeSpan lifetime,
        CancellationToken cancellationToken);

    ValueTask<ReadOnlyMemory<byte>> LoadAsync(
        OwnerId owner,
        ProtectedPayloadReference reference,
        CancellationToken cancellationToken);
}
