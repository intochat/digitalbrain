namespace DigitalBrain.UI;

public interface IKitImageStore
{
    /// <summary>Stores image bytes under a blob name.</summary>
    Task SaveAsync(string blobName, ReadOnlyMemory<byte> content, string mediaType, CancellationToken cancellationToken);

    /// <summary>Reads image bytes and their media type by blob name.</summary>
    Task<(byte[] Content, string MediaType)?> ReadAsync(string blobName, CancellationToken cancellationToken);
}
