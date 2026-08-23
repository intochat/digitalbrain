using System.Collections.Concurrent;

namespace DigitalBrain.UI;

public sealed class MemoryKitImageStore : IKitImageStore
{
    private readonly ConcurrentDictionary<string, (byte[] Content, string MediaType)> _blobs = new(StringComparer.Ordinal);

    public Task SaveAsync(string blobName, ReadOnlyMemory<byte> content, string mediaType, CancellationToken cancellationToken)
    {
        _blobs[blobName] = (content.ToArray(), mediaType);
        return Task.CompletedTask;
    }

    public Task<(byte[] Content, string MediaType)?> ReadAsync(string blobName, CancellationToken cancellationToken)
        => Task.FromResult<(byte[] Content, string MediaType)?>(_blobs.TryGetValue(blobName, out var blob) ? blob : null);
}
