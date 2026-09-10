using System.Collections.Concurrent;

namespace DigitalBrain.UI;

public sealed class MemoryTurnContextStore : ITurnContextBlobStore
{
    private readonly ConcurrentDictionary<string, string> _blobs = new(StringComparer.Ordinal);

    public Task<string> SaveAsync(string digest, string payloadJson, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        _blobs[digest] = payloadJson;
        return Task.FromResult(digest);
    }

    public Task<string?> ReadAsync(string blobRef, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(_blobs.GetValueOrDefault(blobRef));
    }
}
