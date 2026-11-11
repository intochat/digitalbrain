using DigitalBrain.Capabilities;
using DigitalBrain.Capabilities.Storage;
using DigitalBrain.Confirmation;
using DigitalBrain.Operations;
using Xunit;

namespace DigitalBrain.Capabilities.Storage.Tests.Unit;

public sealed class StorageWriteHandlerFacts
{
    private sealed class FakeOwnerSource(CapabilityConnection<IStorage> connection) : IStorageOwnerSource
    {
        public StorageOwner For(BrainId brain, string origin) => (_, _) => Task.FromResult(connection);
    }

    private sealed class FakeStorage : IStorage
    {
        public string? ItemStamp { get; set; } = "stamp-v1";
        public bool WriteCalled { get; private set; }

        public Task<StorageDescriptor> Describe(CancellationToken ct = default) =>
            Task.FromResult(new StorageDescriptor("Fake", "fake", Quota.Unknown, ItemFlags.None));

        public Task<IReadOnlyList<Item>> Roots(CancellationToken ct = default) => Task.FromResult<IReadOnlyList<Item>>([]);
        public Task<Page<Item>> List(ItemId folder, ListQuery query, CancellationToken ct = default) => Task.FromResult(new Page<Item>([], null));
        public Task<Item> Stat(ItemId id, CancellationToken ct = default) =>
            Task.FromResult(new Item(id, "item.txt", ItemKind.File, 100, DateTimeOffset.UtcNow, ItemFlags.Read | ItemFlags.Write, Hash: ItemStamp));
        public Task<Content> Read(ItemId id, ByteRange? range, CancellationToken ct = default) => throw new NotImplementedException();
        public Task<OperationId> Write(WritePlan plan, CancellationToken ct = default)
        {
            WriteCalled = true;
            return Task.FromResult(new OperationId("op-write"));
        }
        public Task<Page<Item>> Search(SearchQuery query, CancellationToken ct = default) => throw new NotImplementedException();
        public Task<IReadOnlyList<Item>> Ancestors(ItemId id, CancellationToken ct = default) => Task.FromResult<IReadOnlyList<Item>>([]);
        public Task<StorageReadStream> OpenRead(ItemId item, ByteRange? range, CancellationToken ct = default) => throw new NotImplementedException();
        public Task<StorageWriteStream> OpenWrite(WritePlan plan, CancellationToken ct = default) => throw new NotImplementedException();
    }

    private sealed class FakeOperationNeuron : IOperationNeuron
    {
        public Task<OperationState> Begin(OperationState initial) => Task.FromResult(initial);
        public Task Prepared(OperationApproval? approval) => Task.CompletedTask;
        public Task<bool> Progress(double fraction) => Task.FromResult(true);
        public Task<OperationState> Complete(OperationResult result) => Task.FromResult(new OperationState());
        public Task<bool> RequestCancel() => Task.FromResult(true);
        public Task Reconcile() => Task.CompletedTask;
        public Task<OperationState?> Get() => Task.FromResult<OperationState?>(null);
    }

    private static OperationScope CreateScope(string? guard = null)
    {
        var services = new ServiceCollection().BuildServiceProvider();
        return new OperationScope(new BrainId("b1"), "test", services, guard, null!);
    }

    private static StorageWriteResult FakeWriteResult => (_, _, _) => Task.FromResult<OperationResult>(new OperationResult.Succeeded());

    [Fact]
    public async Task DescribeReturnsNullForDryRun()
    {
        var storage = new FakeStorage();
        var handler = new StorageWriteHandler(new FakeOwnerSource(new("conn", "storage", storage)), FakeWriteResult);
        var plan = WritePlan.Delete(new ItemId("i-1")) with { DryRun = true };

        var approval = await handler.Describe(plan, CreateScope(), CancellationToken.None);
        Assert.Null(approval);
    }

    [Fact]
    public async Task DescribeReturnsApprovalForDestructivePlan()
    {
        var storage = new FakeStorage { ItemStamp = "hash-123" };
        var handler = new StorageWriteHandler(new FakeOwnerSource(new("conn", "storage", storage)), FakeWriteResult);
        var plan = WritePlan.Delete(new ItemId("i-1"));

        var approval = await handler.Describe(plan, CreateScope(), CancellationToken.None);
        Assert.NotNull(approval);
        Assert.True(approval.Question.Destructive);
        Assert.Equal("hash-123", approval.Guard);
    }

    [Fact]
    public async Task RunFailsWhenGuardStampMismatches()
    {
        var storage = new FakeStorage { ItemStamp = "hash-new" };
        var handler = new StorageWriteHandler(new FakeOwnerSource(new("conn", "storage", storage)), FakeWriteResult);
        var plan = WritePlan.Delete(new ItemId("i-1"));

        var scope = CreateScope(guard: "hash-old");
        var result = await handler.Run(plan, new FakeOperationNeuron(), scope, CancellationToken.None);

        var failed = Assert.IsType<OperationResult.Failed>(result);
        Assert.Equal("plan-invalid", failed.Code);
        Assert.False(storage.WriteCalled);
    }

    [Fact]
    public async Task RunSucceedsWhenGuardMatches()
    {
        var storage = new FakeStorage { ItemStamp = "hash-valid" };
        var handler = new StorageWriteHandler(new FakeOwnerSource(new("conn", "storage", storage)), FakeWriteResult);
        var plan = WritePlan.Delete(new ItemId("i-1"));

        var scope = CreateScope(guard: "hash-valid");
        var result = await handler.Run(plan, new FakeOperationNeuron(), scope, CancellationToken.None);

        Assert.IsType<OperationResult.Succeeded>(result);
        Assert.True(storage.WriteCalled);
    }

    private sealed class ServiceCollection : IServiceProvider
    {
        public object? GetService(Type serviceType) => null;
        public ServiceCollection BuildServiceProvider() => this;
    }
}
