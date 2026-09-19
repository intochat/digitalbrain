using System.Collections.Concurrent;
using Orleans.Runtime;
using Orleans.Storage;
namespace DigitalBrain.Testing;

public sealed class StorageFaults : IDisposable
{
    private readonly ConcurrentDictionary<GrainId, byte> _writes = new();
    private readonly ConcurrentDictionary<GrainId, byte> _reads = new();
    private readonly ConcurrentDictionary<GrainId, ReadHold> _holds = new();
    public void FailNextWrite(GrainId id) => _writes[id] = 1;
    public void FailNextRead(GrainId id) => _reads[id] = 1;
    public ReadHold HoldNextRead(GrainId id)
    {
        var hold = new ReadHold();
        if (!_holds.TryAdd(id, hold)) { throw new InvalidOperationException("A read is already held for this grain."); }
        return hold;
    }
    internal async Task BeforeRead(GrainId id)
    {
        if (_holds.TryGetValue(id, out var hold))
        {
            await hold.WaitAsync().ConfigureAwait(false);
            _holds.TryRemove(id, out _);
        }
        if (_reads.TryRemove(id, out _)) { throw new IOException("Test storage refused a read."); }
    }
    internal void BeforeWrite(GrainId id)
    {
        if (_writes.TryRemove(id, out _)) { throw new IOException("Test storage refused a write."); }
    }
    public void Dispose()
    {
        foreach (var hold in _holds.Values) { hold.Release(); }
        _holds.Clear();
    }
}

public sealed class ReadHold : IDisposable
{
    private readonly TaskCompletionSource _entered = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly TaskCompletionSource _release = new(TaskCreationOptions.RunContinuationsAsynchronously);
    public Task Entered => _entered.Task;
    internal async Task WaitAsync() { _entered.TrySetResult(); await _release.Task.ConfigureAwait(false); }
    public void Release() => _release.TrySetResult();
    public void Dispose() => Release();
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
