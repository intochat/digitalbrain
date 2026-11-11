using System.Threading;
using System.Threading.Tasks;
using DigitalBrain.Capabilities;
using DigitalBrain.Neurons;

namespace DigitalBrain.Testing.Tests.Unit.Apps;

// A test-local storage-like capability.
[Capability("storage-like", 1)]
public interface IStorageLike
{
    string Label { get; }

    Task<long?> Used();

    Task<long?> Total();
}

[Capability("clock-like", 1)]
public interface IClockLike
{
    DateTimeOffset Now();
}

public interface INotACapability
{
    void Nothing();
}

public sealed class NotACapability : INotACapability
{
    public void Nothing() { }
}

public sealed class StorageLikeFake(string label, long? used = null, long? total = null) : IStorageLike
{
    private int _calls;

    public string Label { get; } = label;

    public int Calls => _calls;

    public Task<long?> Used()
    {
        Interlocked.Increment(ref _calls);
        return Task.FromResult(used);
    }

    public Task<long?> Total() => Task.FromResult(total);
}

public interface ISampleCounter
{
    Task<int> Count(int by);

    Task<string> StorageLabel();
}

[Signal("sample-app.counted")]
public sealed record SampleCounted(int Total) : Signal;

public sealed record CounterState
{
    public int Total { get; init; }
}

// A neuron of the sample app: it takes the storage capability, resolved from the bound fakes.
[Neuron("sample-app.counter")]
public sealed class SampleCounter(IStorageLike storage) : Neuron<CounterState>, ISampleCounter
{
    public async Task<int> Count(int by)
    {
        var next = State with { Total = State.Total + by };
        await Save(next, new SampleCounted(next.Total));
        return next.Total;
    }

    public Task<string> StorageLabel() => Task.FromResult(storage.Label);
}
