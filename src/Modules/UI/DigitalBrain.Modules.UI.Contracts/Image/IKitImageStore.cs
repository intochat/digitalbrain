namespace DigitalBrain.UI;

public interface IKitImageStore
{
    Task SaveAsync(string blobName, ReadOnlyMemory<byte> content, string mediaType, CancellationToken cancellationToken);

    Task<(byte[] Content, string MediaType)?> ReadAsync(string blobName, CancellationToken cancellationToken);
}
