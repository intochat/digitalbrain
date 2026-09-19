using System.Collections.Concurrent;
using Orleans.Runtime;
using Orleans.Storage;
namespace DigitalBrain.Testing;

public sealed class StorageFaults : IDisposable
{
    private readonly ConcurrentDictionary<GrainId, bool> _writes = new();
    private readonly ConcurrentDictionary<GrainId, bool> _reads = new();
    public void FailNextWrite(GrainId id) => _writes[id] = true;
    public void FailNextRead(GrainId id) => _reads[id] = true;
    internal Task BeforeRead(GrainId id)
    {
        if (_reads.TryRemove(id, out _)) { throw new IOException("Test storage refused a read."); }
        return Task.CompletedTask;
    }
    internal void BeforeWrite(GrainId id)
    {
        if (_writes.TryRemove(id, out _)) { throw new IOException("Test storage refused a write."); }
    }
    public void Dispose()
    {
        _writes.Clear();
        _reads.Clear();
    }
}

internal sealed class FaultingGrainStorage(IGrainStorage inner, StorageFaults faults) : IGrainStorage
{
    public async Task ReadStateAsync<T>(string name, GrainId id, IGrainState<T> state)
    {
        await faults.BeforeRead(id).ConfigureAwait(false);
        await inner.ReadStateAsync(name, id, state).ConfigureAwait(false);
    }
    public Task WriteStateAsync<T>(string name, GrainId id, IGrainState<T> state)
    {
        faults.BeforeWrite(id);
        return inner.WriteStateAsync(name, id, state);
    }
    public Task ClearStateAsync<T>(string name, GrainId id, IGrainState<T> state)
    {
        faults.BeforeWrite(id);
        return inner.ClearStateAsync(name, id, state);
    }
}
