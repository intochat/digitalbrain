using DigitalBrain.Capabilities;
using DigitalBrain.Capabilities.Storage;
using DigitalBrain.Operations;
using DigitalBrain.Runtime.Orleans.Capabilities;
using DigitalBrain.Runtime.Orleans.Capabilities.Storage;
using DigitalBrain.Testing.Conformance.Storage;
using Microsoft.Extensions.DependencyInjection;

namespace DigitalBrain.Runtime.Orleans.Tests.Integration.Capabilities;

public sealed class StorageAccessFacts
{
    private static readonly BrainId Brain = new("b1");

    [Fact]
    public async Task ListRecordsTheCallingApp()
    {
        var (broker, log, connections) = Broker("test/app-a");
        var storage = await One(broker);
        ((InMemoryStorage)connections.Bound["mem"]).Seed(StorageSeed.Canonical);
        log.Clear();

        await storage.List(((InMemoryStorage)connections.Bound["mem"]).Root, new ListQuery(), TestContext.Current.CancellationToken);

        var row = Assert.Single(log.Rows);
        Assert.Equal("test/app-a", row.App);
        Assert.Equal("b1", row.Brain);
        Assert.Equal("mem", row.ModuleKind);
        Assert.Equal("mem", row.ConnectionKey);
        Assert.Equal("List", row.Op);
        Assert.Equal("Ok", row.Outcome);
    }

    [Fact]
    public async Task ConflictIsRecordedThenRethrown()
    {
        var (broker, log, _) = Broker("test/app-a", new Boom());
        var storage = await One(broker);

        var error = await Assert.ThrowsAsync<Conflict>(() => storage.Stat(new ItemId("m:missing"), TestContext.Current.CancellationToken));

        var row = Assert.Single(log.Rows);
        Assert.Equal("Conflict", row.Outcome);
        Assert.Equal("Stat", row.Op);
        Assert.Equal("conflict", error.Code);
    }

    [Fact]
    public async Task OpenReadRecordsOnceAndOpenWriteRecordsCommit()
    {
        var (broker, log, connections) = Broker("test/app-a");
        var storage = await One(broker);
        var memory = (InMemoryStorage)connections.Bound["mem"];
        memory.Seed(StorageSeed.Canonical);
        var root = memory.Root;
        var plan = (await storage.List(root, new ListQuery(), TestContext.Current.CancellationToken)).Items.Single(item => item.Name == StorageSeed.PlanName);
        log.Clear();

        await using (var read = await storage.OpenRead(plan.Id, null, TestContext.Current.CancellationToken))
        {
            var buffer = new byte[4];
            _ = await read.Content.ReadAsync(buffer, TestContext.Current.CancellationToken);
            _ = await read.Content.ReadAsync(buffer, TestContext.Current.CancellationToken);
        }
        Assert.Equal("OpenRead", Assert.Single(log.Rows).Op);

        log.Clear();
        await using (var write = await storage.OpenWrite(WritePlan.WriteFile(root, "stream.bin"), TestContext.Current.CancellationToken))
        {
            byte[] bytes = [1, 2, 3, 4];
            await write.Content.WriteAsync(bytes, TestContext.Current.CancellationToken);
            _ = await write.Commit(TestContext.Current.CancellationToken);
        }
        Assert.Equal(["OpenWrite", "OpenWrite"], log.Rows.Select(row => row.Op));
        Assert.Equal(["Ok", "Commit"], log.Rows.Select(row => row.Outcome));
    }

    [Fact]
    public async Task ForAppRecordsThatApp()
    {
        var (broker, log, connections) = Broker("runtime");
        var acting = broker.ForApp("test/app-a");
        _ = await One(acting);
        ((InMemoryStorage)connections.Bound["mem"]).Seed(StorageSeed.Canonical);
        log.Clear();

        await (await One(acting)).Describe(TestContext.Current.CancellationToken);

        Assert.Equal("test/app-a", Assert.Single(log.Rows).App);
        Assert.DoesNotContain(log.Rows, row => row.App == "runtime");
    }

    [Fact]
    public async Task RowOmitsPathsAndExceptionText()
    {
        var (broker, log, _) = Broker("test/app-a", new PathBoom());
        var storage = await One(broker);
        var id = new ItemId(@"C:\secret\notes.txt");

        await Assert.ThrowsAsync<InvalidOperationException>(() => storage.Stat(id, TestContext.Current.CancellationToken));

        var row = Assert.Single(log.Rows);
        var text = string.Join(" ", row.App, row.Brain, row.ModuleKind, row.ConnectionKey, row.ItemId, row.Op, row.Outcome);
        Assert.Equal("redacted", row.ItemId);
        Assert.DoesNotContain(@"C:\secret", text, StringComparison.Ordinal);
        Assert.DoesNotContain("boom", text, StringComparison.OrdinalIgnoreCase);
        Assert.Equal("InvalidOperationException", row.Outcome);
    }

    [Fact]
    public async Task ResolvePathsReturnTheDecorator()
    {
        var (broker, _, _) = Broker("test/app-a");
        var ct = TestContext.Current.CancellationToken;
        Assert.IsType<RecordingStorage>((await broker.Storages(Brain, ct)).Single().Capability);
        Assert.IsType<RecordingStorage>((await broker.ResolveAll<IStorage>(Brain, ct)).Single().Capability);
        Assert.IsType<RecordingStorage>((await broker.ResolveFor(Brain, new ItemId("m:x"), ct)).Single().Capability);
    }

    [Fact]
    public async Task JournalPortReceivesTheRedactedRow()
    {
        var (broker, log, _) = Broker("test/app-a", new PathBoom());
        StorageAccessRow? journaled = null;
        log.Journal = row => journaled = row;
        var storage = await One(broker);
        await Assert.ThrowsAsync<InvalidOperationException>(() => storage.Stat(new ItemId("/etc/passwd"), TestContext.Current.CancellationToken));
        Assert.NotNull(journaled);
        Assert.Equal("redacted", journaled.ItemId);
        Assert.Equal("InvalidOperationException", journaled.Outcome);
    }

    private static async Task<IStorage> One(CapabilityBroker broker)
        => (await broker.Storages(Brain, TestContext.Current.CancellationToken)).Single().Capability;

    private static (CapabilityBroker Broker, StorageAccessLog Log, Binder Connections) Broker(string app, IStorage? storage = null)
    {
        var log = new StorageAccessLog();
        var connections = new Binder(storage);
        var services = new ServiceCollection().AddSingleton(log).BuildServiceProvider();
        var broker = new CapabilityBroker(new CapabilityRegistry(), connections, services, []).ForApp(app);
        return (broker, log, connections);
    }

    private sealed class Binder(IStorage? storage) : ICapabilityConnections
    {
        internal Dictionary<string, object> Bound { get; } = [];

        public Task<IReadOnlyList<BrainConnection>> ListInOrder(BrainId brain, CancellationToken ct)
            => Task.FromResult<IReadOnlyList<BrainConnection>>([new BrainConnection("mem", "Memory", true, [Capability.Storage.V1], "m:")]);

        public Task<TCapability> Bind<TCapability>(BrainId brain, string key, string callerApp, CancellationToken ct) where TCapability : class
        {
            if (!Bound.TryGetValue(key, out var bound)) { Bound[key] = bound = storage ?? new InMemoryStorage(key); }
            return Task.FromResult((TCapability)bound);
        }

        public Task RequestConnection(BrainId brain, CapabilityId capability, string callerApp, CancellationToken ct) => Task.CompletedTask;
    }

    private sealed class Boom : IStorage
    {
        public Task<Item> Stat(ItemId id, CancellationToken ct = default) => throw new Conflict(id);
        public Task<StorageDescriptor> Describe(CancellationToken ct = default) => throw new NotSupportedException();
        public Task<IReadOnlyList<Item>> Roots(CancellationToken ct = default) => throw new NotSupportedException();
        public Task<Page<Item>> List(ItemId folder, ListQuery query, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<Content> Read(ItemId id, ByteRange? range, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<OperationId> Write(WritePlan plan, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<Page<Item>> Search(SearchQuery query, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<IReadOnlyList<Item>> Ancestors(ItemId id, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<StorageReadStream> OpenRead(ItemId item, ByteRange? range, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<StorageWriteStream> OpenWrite(WritePlan plan, CancellationToken ct = default) => throw new NotSupportedException();
    }

    private sealed class PathBoom : IStorage
    {
        public Task<Item> Stat(ItemId id, CancellationToken ct = default) => throw new InvalidOperationException(@"C:\secret\boom");
        public Task<StorageDescriptor> Describe(CancellationToken ct = default) => throw new NotSupportedException();
        public Task<IReadOnlyList<Item>> Roots(CancellationToken ct = default) => throw new NotSupportedException();
        public Task<Page<Item>> List(ItemId folder, ListQuery query, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<Content> Read(ItemId id, ByteRange? range, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<OperationId> Write(WritePlan plan, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<Page<Item>> Search(SearchQuery query, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<IReadOnlyList<Item>> Ancestors(ItemId id, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<StorageReadStream> OpenRead(ItemId item, ByteRange? range, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<StorageWriteStream> OpenWrite(WritePlan plan, CancellationToken ct = default) => throw new NotSupportedException();
    }
}
