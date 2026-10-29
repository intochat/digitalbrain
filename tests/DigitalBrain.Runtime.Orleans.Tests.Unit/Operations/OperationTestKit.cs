using System.Collections.Concurrent;
using System.Text;
using DigitalBrain.Capabilities;
using DigitalBrain.Capabilities.Storage;
using DigitalBrain.Operations;
using DigitalBrain.Spi;
using DigitalBrain.Testing.Conformance.Storage;

namespace DigitalBrain.Runtime.Orleans.Tests.Unit.Operations;

/// <summary>An operation handle that records reports and lets a fact cancel it, optionally from inside a report.</summary>
internal sealed class RecordingOperation : IOperation, IDisposable
{
    private readonly CancellationTokenSource _cancellation = new();
    private readonly ConcurrentQueue<double> _reports = new();

    public OperationId Id { get; } = new("rec-" + Guid.NewGuid().ToString("N"));

    public CancellationToken Cancellation => _cancellation.Token;

    public IReadOnlyList<double> Reports => [.. _reports];

    /// <summary>Called on each report; return true to cancel right after it.</summary>
    public Func<double, int, bool>? CancelWhen { get; init; }

    public Func<double, Task>? Reported { get; init; }

    public int ReportsAfterCancel { get; private set; }

    public async ValueTask Report(double fraction)
    {
        if (_cancellation.IsCancellationRequested) { ReportsAfterCancel++; }
        _reports.Enqueue(fraction);
        if (Reported is not null) { await Reported(fraction); }
        if (CancelWhen?.Invoke(fraction, _reports.Count) == true) { _cancellation.Cancel(); }
    }

    public ValueTask Complete(OperationResult result) => ValueTask.CompletedTask;

    public void Cancel() => _cancellation.Cancel();

    public void Dispose() => _cancellation.Dispose();
}

/// <summary>
/// A storage@1 decorator over <see cref="InMemoryStorage"/> for fault injection: corrupt committed writes, refuse deletes, slow
/// writes, and a shared call log (<c>label:Member[:kind]</c>) to check ordering across connections.
/// </summary>
internal sealed class FaultyStorage(InMemoryStorage inner, ConcurrentQueue<string>? log = null) : IStorage
{
    public InMemoryStorage Inner { get; } = inner;

    public ConcurrentQueue<string> Log { get; } = log ?? new ConcurrentQueue<string>();

    /// <summary>Flip the first byte of every committed streaming write.</summary>
    public bool CorruptWrites { get; set; }

    /// <summary>Refuse deletes with <see cref="Unavailable"/>.</summary>
    public bool FailDeletes { get; set; }
    public bool HideHashes { get; set; }
    public bool RefuseWholeReads { get; set; }
    public bool FailReads { get; set; }

    /// <summary>Delay before each streaming write opens.</summary>
    public TimeSpan WriteDelay { get; set; }

    private void Record(string member) => Log.Enqueue($"{Inner.Label}:{member}");

    public Task<StorageDescriptor> Describe(CancellationToken ct = default) => Inner.Describe(ct);

    public Task<IReadOnlyList<Item>> Roots(CancellationToken ct = default) => Inner.Roots(ct);

    public Task<Page<Item>> List(ItemId folder, ListQuery query, CancellationToken ct = default) => Inner.List(folder, query, ct);

    public async Task<Item> Stat(ItemId id, CancellationToken ct = default)
    {
        Record("Stat");
        var item = await Inner.Stat(id, ct);
        return HideHashes ? item with { Hash = null } : item;
    }

    public Task<Content> Read(ItemId id, ByteRange? range, CancellationToken ct = default)
    {
        if (RefuseWholeReads && range is null) { throw new Unsupported(id); }
        return Inner.Read(id, range, ct);
    }

    public Task<OperationId> Write(WritePlan plan, CancellationToken ct = default)
    {
        Record("Write:" + plan.Kind);
        if (FailDeletes && plan.Kind == WriteKind.Delete) { throw new Unavailable(plan.Source); }
        return Inner.Write(plan, ct);
    }

    public Task<Page<Item>> Search(SearchQuery query, CancellationToken ct = default) => Inner.Search(query, ct);

    public Task<IReadOnlyList<Item>> Ancestors(ItemId id, CancellationToken ct = default) => Inner.Ancestors(id, ct);

    public Task<StorageReadStream> OpenRead(ItemId item, ByteRange? range, CancellationToken ct = default)
        => FailReads ? throw new Unavailable(item) : Inner.OpenRead(item, range, ct);

    public async Task<StorageWriteStream> OpenWrite(WritePlan plan, CancellationToken ct = default)
    {
        Record("OpenWrite");
        if (WriteDelay > TimeSpan.Zero) { await Task.Delay(WriteDelay, ct); }
        var write = await Inner.OpenWrite(plan, ct);
        return CorruptWrites ? new Corrupting(write) : write;
    }

    private sealed class Corrupting(StorageWriteStream inner) : StorageWriteStream(new MemoryStream())
    {
        protected override async Task<ItemId> CommitCore(CancellationToken ct)
        {
            var bytes = ((MemoryStream)Content).ToArray();
            if (bytes.Length > 0) { bytes[0] ^= 0xFF; }
            await inner.Content.WriteAsync(bytes, ct);
            return await inner.Commit(ct);
        }

        protected override Task AbortCore(CancellationToken ct) => inner.Abort(ct);
    }
}

/// <summary>Named storage connections for the runner: owner by Stat, results from the in-memory operations.</summary>
internal sealed class Connections
{
    private readonly List<CapabilityConnection<IStorage>> _all = [];

    public Connections Add(string key, IStorage storage)
    {
        _all.Add(new CapabilityConnection<IStorage>(key, key, storage));
        return this;
    }

    public async Task<CapabilityConnection<IStorage>> Owner(ItemId item, CancellationToken ct)
    {
        foreach (var connection in _all)
        {
            try
            {
                _ = await Unwrap(connection.Capability).Stat(item, ct);
                return connection;
            }
            catch (NotFound)
            {
            }
        }
        throw new NotFound(item);
    }

    public static Task<OperationResult> Result(IStorage storage, OperationId id, CancellationToken ct) => Unwrap(storage).Operations.WaitFor(id, ct);

    public StorageWriteRunner Runner() => new(Owner, Result);

    private static InMemoryStorage Unwrap(IStorage storage) => storage switch
    {
        InMemoryStorage memory => memory,
        FaultyStorage faulty => faulty.Inner,
        _ => throw new NotSupportedException(storage.GetType().Name),
    };
}

internal static class Trees
{
    public static byte[] Bytes(string text) => Encoding.UTF8.GetBytes(text);

    public static async Task<List<Item>> Children(this IStorage storage, ItemId folder)
        => [.. (await storage.List(folder, new ListQuery(null, ListQuery.MaxPageSize))).Items];

    public static async Task<Item?> Named(this IStorage storage, ItemId folder, string name)
        => (await storage.Children(folder)).SingleOrDefault(item => item.Name == name);

    public static async Task<string> Text(this IStorage storage, ItemId file) => await storage.ReadText(file);
}
