using DigitalBrain.Capabilities;
using DigitalBrain.UI;
using System.Collections.Concurrent;

namespace DigitalBrain.Testing.Tests.Unit.Apps;

// A test-local storage-like capability: the Phase 5 storage contract is not here yet.
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

// Requires storage-like@1 through its constructor; "open" calls the fake and a neuron of the app.
[App("sample-app")]
public sealed class SampleApp(IStorageLike storage, ICapabilityBroker broker) : App
{
    public ConcurrentQueue<string> Log { get; } = new();

    [On]
    public async Task Open()
    {
        var used = await storage.Used();
        var resolved = await broker.Resolve<IStorageLike>(new BrainId("ada"), CancellationToken.None);
        Log.Enqueue($"open:{storage.Label}:{used}:{ReferenceEquals(resolved, storage)}");
    }

    [On]
    public Task Rename(string name)
    {
        Log.Enqueue($"rename:{name}");
        return Task.CompletedTask;
    }
}

// Declares its manifest: requires a capability the facts never bind.
[App("needs-clock")]
public sealed class NeedsClockApp : App
{
    public static AppManifest Manifest { get; } = new("needs-clock", "Needs clock", "1.0.0", [], new AppRequirements([CapabilityId.Parse("clock-like@1")]));

    public int Opened { get; private set; }

    [On]
    public Task Open()
    {
        Opened++;
        return Task.CompletedTask;
    }
}

// DESIGN section 6, ported to the test-local storage-like capability: every bound storage on the side with its quota as trailing text.
[App("files-like")]
public sealed class FilesLike(IReadOnlyList<IStorageLike> storages, IPresenter presenter) : App
{
    [On]
    public async Task Open()
    {
        var entries = new List<Entry>();
        foreach (var storage in storages)
        {
            entries.Add(new Entry(storage.Label, storage.Label, null) { Trailing = Quota(await storage.Used(), await storage.Total()) });
        }
        presenter.Present(
            new View("Storages", new Collection(entries)),
            new View("Files", new Column([new TextField("filter", null, "Filter names"), new Collection([]), new Text("content")])));
    }

    [On]
    public Task Search(string text)
    {
        presenter.Toast(new Toast($"searching '{text}'"));
        presenter.Present(null, new View("Results", new Column([new Text($"results for '{text}'")])));
        return Task.CompletedTask;
    }

    private static string Quota(long? used, long? total)
        => used is { } u && total is { } t ? $"{u} GB of {t} GB" : "quota unknown";
}

// Presents through its own Side and Main properties, read after every turn.
[App("tally")]
public sealed class TallyApp : App
{
    private int _count;

    public View Side => new("Tally", new Collection([new Entry("count", "Count", null) { Trailing = _count.ToString(System.Globalization.CultureInfo.InvariantCulture) }]));

    public Node Main => new Text($"count is {_count}");

    [On]
    public Task Add()
    {
        _count++;
        return Task.CompletedTask;
    }
}
