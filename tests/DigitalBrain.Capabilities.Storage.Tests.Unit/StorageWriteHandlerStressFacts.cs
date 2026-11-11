using System.Globalization;
using DigitalBrain.Capabilities;
using DigitalBrain.Capabilities.Storage;
using DigitalBrain.Confirmation;
using DigitalBrain.Operations;
using Xunit;

namespace DigitalBrain.Capabilities.Storage.Tests.Unit;

/// <summary>
/// Empirical stress-tests for StorageWriteHandler stale guard rejection and concurrency.
/// </summary>
public sealed class StorageWriteHandlerStressFacts
{
    private sealed class MockStorage : IStorage
    {
        private int _writeCallCount;

        public string? ItemHash { get; set; } = "hash-v1";
        public DateTimeOffset? ItemModified { get; set; }
        public bool ThrowOnStat { get; set; }
        public int WriteCallCount => _writeCallCount;

        public Task<StorageDescriptor> Describe(CancellationToken ct = default) =>
            Task.FromResult(new StorageDescriptor("Mock", "mock", Quota.Unknown, ItemFlags.None));

        public Task<IReadOnlyList<Item>> Roots(CancellationToken ct = default) => Task.FromResult<IReadOnlyList<Item>>([]);
        public Task<Page<Item>> List(ItemId folder, ListQuery query, CancellationToken ct = default) => Task.FromResult(new Page<Item>([], null));

        public Task<Item> Stat(ItemId id, CancellationToken ct = default)
        {
            if (ThrowOnStat)
            {
                throw new NotFound(id);
            }
            return Task.FromResult(new Item(id, "target.txt", ItemKind.File, 100, ItemModified ?? DateTimeOffset.UtcNow, ItemFlags.Read | ItemFlags.Write, Hash: ItemHash));
        }

        public Task<Content> Read(ItemId id, ByteRange? range, CancellationToken ct = default) => throw new NotImplementedException();

        public Task<OperationId> Write(WritePlan plan, CancellationToken ct = default)
        {
            Interlocked.Increment(ref _writeCallCount);
            return Task.FromResult(new OperationId("op-write-1"));
        }

        public Task<Page<Item>> Search(SearchQuery query, CancellationToken ct = default) => throw new NotImplementedException();
        public Task<IReadOnlyList<Item>> Ancestors(ItemId id, CancellationToken ct = default) => Task.FromResult<IReadOnlyList<Item>>([]);
        public Task<StorageReadStream> OpenRead(ItemId item, ByteRange? range, CancellationToken ct = default) => throw new NotImplementedException();
        public Task<StorageWriteStream> OpenWrite(WritePlan plan, CancellationToken ct = default) => throw new NotImplementedException();
    }

    private sealed class MockOwnerSource(CapabilityConnection<IStorage> connection) : IStorageOwnerSource
    {
        public StorageOwner For(BrainId brain, string origin) => (_, _) => Task.FromResult(connection);
    }

    private sealed class TrackingOperationNeuron : IOperationNeuron
    {
        public double LastProgress { get; private set; }
        public OperationResult? FinalResult { get; private set; }

        public Task<OperationState> Begin(OperationState initial) => Task.FromResult(initial);
        public Task Prepared(OperationApproval? approval) => Task.CompletedTask;
        public Task<bool> Progress(double fraction)
        {
            LastProgress = fraction;
            return Task.FromResult(true);
        }
        public Task<OperationState> Complete(OperationResult result)
        {
            FinalResult = result;
            return Task.FromResult(new OperationState());
        }
        public Task<bool> RequestCancel() => Task.FromResult(true);
        public Task Reconcile() => Task.CompletedTask;
        public Task<OperationState?> Get() => Task.FromResult<OperationState?>(null);
    }

    private sealed class MockServiceProvider(params (Type Type, object Instance)[] services) : IServiceProvider
    {
        public object? GetService(Type serviceType) =>
            services.FirstOrDefault(s => s.Type == serviceType).Instance;
    }

    private static OperationScope CreateScope(IServiceProvider services, string? guard = null) =>
        new(new BrainId("b1"), "test-caller", services, guard, null!);

    private static StorageWriteResult SucceededWriteResult => (_, _, _) => Task.FromResult<OperationResult>(new OperationResult.Succeeded());

    [Fact]
    public async Task StaleGuard_WhenContentHashModified_RejectsExecutionImmediately()
    {
        var storage = new MockStorage { ItemHash = "hash-v1" };
        var handler = new StorageWriteHandler(new MockOwnerSource(new("conn", "storage", storage)), SucceededWriteResult);
        var plan = WritePlan.Delete(new ItemId("i-target"));

        // Describe captures guard = "hash-v1"
        var sp = new MockServiceProvider();
        var approval = await handler.Describe(plan, CreateScope(sp), CancellationToken.None);
        Assert.NotNull(approval);
        Assert.Equal("hash-v1", approval.Guard);

        // Mutate storage content before Run executes
        storage.ItemHash = "hash-v2-modified";

        var neuron = new TrackingOperationNeuron();
        var scopeWithGuard = CreateScope(sp, guard: approval.Guard);
        var result = await handler.Run(plan, neuron, scopeWithGuard, CancellationToken.None);

        var failed = Assert.IsType<OperationResult.Failed>(result);
        Assert.Equal("plan-invalid", failed.Code);
        Assert.Equal("The item changed.", failed.Message);
        Assert.Equal(0, storage.WriteCallCount);
    }

    [Fact]
    public async Task StaleGuard_WhenTimestampModifiedAndNoHash_RejectsExecution()
    {
        var t1 = new DateTimeOffset(2026, 11, 11, 10, 0, 0, TimeSpan.Zero);
        var t2 = new DateTimeOffset(2026, 11, 11, 10, 5, 0, TimeSpan.Zero);

        var storage = new MockStorage { ItemHash = null, ItemModified = t1 };
        var handler = new StorageWriteHandler(new MockOwnerSource(new("conn", "storage", storage)), SucceededWriteResult);
        var plan = WritePlan.Delete(new ItemId("i-target"));

        var sp = new MockServiceProvider();
        var approval = await handler.Describe(plan, CreateScope(sp), CancellationToken.None);
        Assert.NotNull(approval);
        Assert.Equal(t1.UtcTicks.ToString(CultureInfo.InvariantCulture), approval.Guard);

        // Touch the item timestamp
        storage.ItemModified = t2;

        var neuron = new TrackingOperationNeuron();
        var scopeWithGuard = CreateScope(sp, guard: approval.Guard);
        var result = await handler.Run(plan, neuron, scopeWithGuard, CancellationToken.None);

        var failed = Assert.IsType<OperationResult.Failed>(result);
        Assert.Equal("plan-invalid", failed.Code);
        Assert.Equal(0, storage.WriteCallCount);
    }

    [Fact]
    public async Task StaleGuard_WhenItemDeletedBeforeRun_RejectsExecution()
    {
        var storage = new MockStorage { ItemHash = "hash-existing" };
        var handler = new StorageWriteHandler(new MockOwnerSource(new("conn", "storage", storage)), SucceededWriteResult);
        var plan = WritePlan.Delete(new ItemId("i-target"));

        var sp = new MockServiceProvider();
        var approval = await handler.Describe(plan, CreateScope(sp), CancellationToken.None);
        Assert.NotNull(approval);

        // Delete the item (Stat throws NotFound StorageException)
        storage.ThrowOnStat = true;

        var neuron = new TrackingOperationNeuron();
        var scopeWithGuard = CreateScope(sp, guard: approval.Guard);
        var result = await handler.Run(plan, neuron, scopeWithGuard, CancellationToken.None);

        var failed = Assert.IsType<OperationResult.Failed>(result);
        Assert.Equal("plan-invalid", failed.Code);
        Assert.Equal(0, storage.WriteCallCount);
    }

    [Fact]
    public async Task FreshGuard_WhenItemUnchanged_ExecutesWriteSuccessfully()
    {
        var storage = new MockStorage { ItemHash = "hash-constant" };
        var handler = new StorageWriteHandler(new MockOwnerSource(new("conn", "storage", storage)), SucceededWriteResult);
        var plan = WritePlan.Delete(new ItemId("i-target"));

        var sp = new MockServiceProvider();
        var approval = await handler.Describe(plan, CreateScope(sp), CancellationToken.None);
        Assert.NotNull(approval);

        var neuron = new TrackingOperationNeuron();
        var scopeWithGuard = CreateScope(sp, guard: approval.Guard);
        var result = await handler.Run(plan, neuron, scopeWithGuard, CancellationToken.None);

        Assert.IsType<OperationResult.Succeeded>(result);
        Assert.Equal(1, storage.WriteCallCount);
    }

    [Fact]
    public async Task NullGuard_DoesNotRejectPlanWithoutGuard()
    {
        var storage = new MockStorage { ItemHash = "hash-1" };
        var handler = new StorageWriteHandler(new MockOwnerSource(new("conn", "storage", storage)), SucceededWriteResult);
        var plan = WritePlan.Delete(new ItemId("i-target"));

        var sp = new MockServiceProvider();
        var neuron = new TrackingOperationNeuron();
        // Scope with guard = null
        var scopeWithoutGuard = CreateScope(sp, guard: null);
        var result = await handler.Run(plan, neuron, scopeWithoutGuard, CancellationToken.None);

        Assert.IsType<OperationResult.Succeeded>(result);
        Assert.Equal(1, storage.WriteCallCount);
    }

    [Fact]
    public async Task ConcurrentGuardStress_RacingModificationsSafelyPartitionValidAndStaleRuns()
    {
        var storage = new MockStorage { ItemHash = "hash-v1" };
        var handler = new StorageWriteHandler(new MockOwnerSource(new("conn", "storage", storage)), SucceededWriteResult);
        var plan = WritePlan.Delete(new ItemId("i-target"));
        var sp = new MockServiceProvider();

        const string approvedGuard = "hash-v1";
        var succeeded = 0;
        var rejected = 0;

        var tasks = Enumerable.Range(0, 50).Select(async i =>
        {
            if (i == 25)
            {
                // Invalidate mid-stream
                storage.ItemHash = "hash-v2-stale";
            }

            var neuron = new TrackingOperationNeuron();
            var result = await handler.Run(plan, neuron, CreateScope(sp, guard: approvedGuard), CancellationToken.None);

            if (result is OperationResult.Succeeded)
            {
                Interlocked.Increment(ref succeeded);
            }
            else if (result is OperationResult.Failed failed && failed.Code == "plan-invalid")
            {
                Interlocked.Increment(ref rejected);
            }
        });

        await Task.WhenAll(tasks);

        Assert.Equal(50, succeeded + rejected);
        Assert.True(rejected > 0, "At least some operations should have been rejected as stale after hash modification.");
        Assert.Equal(succeeded, storage.WriteCallCount);
    }
}
