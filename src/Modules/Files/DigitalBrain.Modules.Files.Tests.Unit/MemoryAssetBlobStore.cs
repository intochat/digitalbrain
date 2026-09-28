namespace DigitalBrain.Files.Tests;
internal sealed class MemoryAssetBlobStore : IAssetBlobStore
{
    internal Dictionary<string, byte[]> Values { get; } = [];
    internal bool FailNextUpload { get; set; }
    public Task Put(string id, byte[] bytes, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        if (FailNextUpload) { FailNextUpload = false; throw new IOException("Storage unavailable"); }
        if (Values.TryGetValue(id, out var previous) && !previous.SequenceEqual(bytes)) { throw new IOException("Immutable conflict"); }
        Values[id] = bytes.ToArray();
        return Task.CompletedTask;
    }
    public Task<byte[]?> Read(string id, CancellationToken ct) => Task.FromResult(Values.GetValueOrDefault(id)?.ToArray());
}
