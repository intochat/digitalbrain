namespace DigitalBrain.UI;

public interface ITurnContextBlobStore
{
    Task<string> SaveAsync(string digest, string payloadJson, CancellationToken cancellationToken);
    Task<string?> ReadAsync(string blobRef, CancellationToken cancellationToken);
}
