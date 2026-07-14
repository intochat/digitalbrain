using System.Collections.Concurrent;
namespace DigitalBrain.Kernel.Config;

internal sealed class InMemoryIntegrationConfigBackingStore : IIntegrationConfigBackingStore
{
    private readonly ConcurrentDictionary<string, byte[]> _store = new();
    private static string BlobKey(string scope, string pack) => $"{scope}/{pack}";
    public Task<byte[]?> LoadAsync(string scope, string pack, CancellationToken cancellationToken = default)
        => Task.FromResult<byte[]?>(_store.TryGetValue(BlobKey(scope, pack), out var blob) ? blob : null);
    public Task SaveAsync(string scope, string pack, byte[] encryptedBlob, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        _store[BlobKey(scope, pack)] = encryptedBlob;
        return Task.CompletedTask;
    }
    public byte[]? Peek(string scope, string pack)
            => _store.TryGetValue(BlobKey(scope, pack), out var blob) ? blob : null;
}
