using DigitalBrain.Capabilities;
using DigitalBrain.Capabilities.Storage;
using DigitalBrain.Confirmation;
using DigitalBrain.Operations;
using DigitalBrain.Runtime.Orleans.Capabilities;
using DigitalBrain.Runtime.Orleans.Capabilities.Storage;
using DigitalBrain.Runtime.Orleans.Operations;
using DigitalBrain.Testing;
using Microsoft.Extensions.DependencyInjection;

namespace DigitalBrain.Runtime.Orleans.Tests.Integration.Operations;

public sealed class StorageOperationGateFacts
{
    private const string App = "app.files";
    private static readonly BrainId Brain = new("b1");

    [Fact]
    public async Task DeleteUnderDestructiveOnlyAsksAndADenialChangesNothing()
    {
        var (gate, confirmations, storage, _) = Gate("Drive");
        var file = storage.AddFile(storage.Root, "a.txt", "hello"u8.ToArray());
        var running = gate.Run(WritePlan.Delete(file), TestContext.Current.CancellationToken);

        var request = await Wait(confirmations);
        Assert.Equal((ConfirmationAction.Delete, true), (request.Plan.Action, request.Plan.Destructive));
        await confirmations.Deny(request.PlanId);

        var done = new OperationDone(new OperationId("op-delete"), await running);
        Assert.Equal("declined", Assert.IsType<OperationResult.Failed>(done.Result).Code);
        Assert.DoesNotContain(storage.Calls, call => call is "Write" or "OpenWrite" or "OpenRead");
        Assert.Equal("a.txt", (await storage.Stat(file, TestContext.Current.CancellationToken)).Name);
    }

    [Fact]
    public async Task CrossConnectionMoveCopiesVerifiesAndDeletesTheSource()
    {
        var left = new FakeStorage("Drive");
        var right = new FakeStorage("Disk");
        var (gate, confirmations) = Gate(left, right);
        var file = left.AddFile(left.Root, "a.txt", "moving"u8.ToArray());
        var running = gate.Run(WritePlan.Move(file, right.Root), TestContext.Current.CancellationToken);

        var request = await Wait(confirmations);
        Assert.Equal(ConfirmationAction.Move, request.Plan.Action);
        Assert.Equal("Move a.txt to Disk?", request.Plan.Title);
        Assert.Equal(("Move", "Cancel"), (request.Plan.ApproveLabel, request.Plan.DenyLabel));
        await confirmations.Approve(request.PlanId);

        Assert.IsType<OperationResult.Succeeded>(await running);
        Assert.Equal("moving", await right.ReadText((await right.List(right.Root, new ListQuery(), TestContext.Current.CancellationToken)).Items.Single(item => item.Name == "a.txt").Id, TestContext.Current.CancellationToken));
        Assert.DoesNotContain((await left.List(left.Root, new ListQuery(), TestContext.Current.CancellationToken)).Items, item => item.Name == "a.txt");
    }

    [Fact]
    public async Task VerifyMismatchKeepsTheSourceAndRemovesThePartial()
    {
        var left = new FakeStorage("Drive");
        var right = new Inflated(new FakeStorage("Disk"));
        var (gate, confirmations) = Gate(left, right);
        var file = left.AddFile(left.Root, "a.txt", "moving"u8.ToArray());
        var running = gate.Run(WritePlan.Move(file, right.Inner.Root), TestContext.Current.CancellationToken);

        await confirmations.Approve((await Wait(confirmations)).PlanId);

        var failed = await Assert.ThrowsAsync<VerifyFailed>(() => running);
        Assert.Equal("verify-failed", failed.Code);
        Assert.Contains((await left.List(left.Root, new ListQuery(), TestContext.Current.CancellationToken)).Items, item => item.Name == "a.txt");
        Assert.DoesNotContain((await right.Inner.List(right.Inner.Root, new ListQuery(), TestContext.Current.CancellationToken)).Items, item => item.Name == "a.txt");
    }

    [Fact]
    public async Task OverwriteAsksToReplace()
    {
        var (gate, confirmations, storage, _) = Gate("Drive");
        storage.AddFile(storage.Root, "notes.txt", "old"u8.ToArray());
        var running = gate.Run(WritePlan.WriteFile(storage.Root, "notes.txt", "new"u8.ToArray(), ConflictPolicy.Overwrite), TestContext.Current.CancellationToken);

        var request = await Wait(confirmations);
        Assert.Equal(ConfirmationAction.Overwrite, request.Plan.Action);
        Assert.Equal("Replace notes.txt?", request.Plan.Title);
        Assert.Equal(("Replace", "Keep existing"), (request.Plan.ApproveLabel, request.Plan.DenyLabel));
        await confirmations.Deny(request.PlanId);
        Assert.Equal("declined", Assert.IsType<OperationResult.Failed>(await running).Code);
    }

    [Fact]
    public async Task RenameCreateFolderAndSameConnectionCopyDoNotAsk()
    {
        var (gate, confirmations, storage, _) = Gate("Drive");
        var folder = storage.AddFolder(storage.Root, "Inbox");
        var file = storage.AddFile(storage.Root, "a.txt", "x"u8.ToArray());

        Assert.IsType<OperationResult.Succeeded>(await gate.Run(WritePlan.Rename(file, "b.txt"), TestContext.Current.CancellationToken));
        Assert.IsType<OperationResult.Succeeded>(await gate.Run(WritePlan.CreateFolder(storage.Root, "New"), TestContext.Current.CancellationToken));
        Assert.IsType<OperationResult.Succeeded>(await gate.Run(WritePlan.Copy(file, folder), TestContext.Current.CancellationToken));

        Assert.Empty(confirmations.Audit);
        Assert.Empty(confirmations.Requested);
    }

    [Fact]
    public async Task ApprovalAfterTheItemChangedIsPlanInvalid()
    {
        var (gate, confirmations, storage, _) = Gate("Drive");
        var file = storage.AddFile(storage.Root, "a.txt", "before"u8.ToArray());
        var running = gate.Run(WritePlan.Delete(file), TestContext.Current.CancellationToken);
        await Wait(confirmations);
        await storage.Write(WritePlan.WriteFile(storage.Root, "a.txt", "after"u8.ToArray(), ConflictPolicy.Overwrite), TestContext.Current.CancellationToken);
        await confirmations.Approve(confirmations.Waiting[0].PlanId);

        var failed = Assert.IsType<OperationResult.Failed>(await running);
        Assert.Equal("plan-invalid", failed.Code);
        Assert.Equal("after", await storage.ReadText(file, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task ApprovedDeleteIsLoggedForTheRequestingApp()
    {
        var (gate, confirmations, storage, log) = Gate("Drive");
        var file = storage.AddFile(storage.Root, "a.txt", "hello"u8.ToArray());
        var running = gate.Run(WritePlan.Delete(file, permanent: true), TestContext.Current.CancellationToken);
        var request = await Wait(confirmations);
        Assert.Contains("You cannot undo this.", request.Plan.Body, StringComparison.Ordinal);
        log.Clear();
        await confirmations.Approve(request.PlanId);

        Assert.IsType<OperationResult.Succeeded>(await running);
        Assert.Contains(log.Rows, row => row.Op == "Stat" && row.App == App);
        Assert.Contains(log.Rows, row => row.Op == "Write" && row.App == App);
        Assert.DoesNotContain((await storage.List(storage.Root, new ListQuery(), TestContext.Current.CancellationToken)).Items, item => item.Name == "a.txt");
    }

    private static (StorageOperationGate Gate, TestConfirmations Confirmations, FakeStorage Storage, StorageAccessLog Log) Gate(string label)
    {
        var storage = new FakeStorage(label);
        var (gate, confirmations, log) = Open([storage]);
        return (gate, confirmations, storage, log);
    }

    private static (StorageOperationGate Gate, TestConfirmations Confirmations) Gate(IStorage left, IStorage right)
    {
        var (gate, confirmations, _) = Open([left, right]);
        return (gate, confirmations);
    }

    private static (StorageOperationGate Gate, TestConfirmations Confirmations, StorageAccessLog Log) Open(IReadOnlyList<IStorage> storages)
    {
        var log = new StorageAccessLog();
        var connections = new Binder(storages);
        var services = new ServiceCollection().AddSingleton(log).BuildServiceProvider();
        var broker = new CapabilityBroker(new CapabilityRegistry(), connections, services, []).ForApp(App);
        var confirmations = new TestConfirmations(TimeProvider.System);
        return (new StorageOperationGate(broker, confirmations.For(ConfirmationPrincipal.App(App)), Brain), confirmations, log);
    }

    private static async Task<ConfirmationRequested> Wait(TestConfirmations confirmations)
    {
        var ct = TestContext.Current.CancellationToken;
        for (var i = 0; i < 500; i++)
        {
            if (confirmations.Waiting.Count > 0) { return confirmations.Waiting[0]; }
            await Task.Delay(10, ct);
        }
        throw new TimeoutException("Nothing was asked.");
    }

    private sealed class Binder(IReadOnlyList<IStorage> storages) : ICapabilityConnections
    {
        public Task<IReadOnlyList<BrainConnection>> ListInOrder(BrainId brain, CancellationToken ct)
            => Task.FromResult<IReadOnlyList<BrainConnection>>([.. storages.Select((storage, index) => new BrainConnection("s" + index, Label(storage), true, [Capability.Storage.V1], null))]);

        public Task<TCapability> Bind<TCapability>(BrainId brain, string key, string callerApp, CancellationToken ct) where TCapability : class
        {
            var index = int.Parse(key[1..], System.Globalization.CultureInfo.InvariantCulture);
            return Task.FromResult((TCapability)storages[index]);
        }

        public Task RequestConnection(BrainId brain, CapabilityId capability, string callerApp, CancellationToken ct) => Task.CompletedTask;

        private static string Label(IStorage storage) => storage switch
        {
            FakeStorage fake => fake.Label,
            Inflated inflated => inflated.Inner.Label,
            _ => "storage",
        };
    }

    private sealed class Inflated(FakeStorage inner) : IStorage
    {
        public FakeStorage Inner { get; } = inner;

        public Task<Item> Stat(ItemId id, CancellationToken ct = default)
            => Inflate(Inner.Stat(id, ct));

        public Task<StorageDescriptor> Describe(CancellationToken ct = default) => Inner.Describe(ct);
        public Task<IReadOnlyList<Item>> Roots(CancellationToken ct = default) => Inner.Roots(ct);
        public Task<Page<Item>> List(ItemId folder, ListQuery query, CancellationToken ct = default) => Inner.List(folder, query, ct);
        public Task<Content> Read(ItemId id, ByteRange? range, CancellationToken ct = default) => Inner.Read(id, range, ct);
        public Task<OperationId> Write(WritePlan plan, CancellationToken ct = default) => Inner.Write(plan, ct);
        public Task<Page<Item>> Search(SearchQuery query, CancellationToken ct = default) => Inner.Search(query, ct);
        public Task<IReadOnlyList<Item>> Ancestors(ItemId id, CancellationToken ct = default) => Inner.Ancestors(id, ct);
        public Task<StorageReadStream> OpenRead(ItemId item, ByteRange? range, CancellationToken ct = default) => Inner.OpenRead(item, range, ct);
        public Task<StorageWriteStream> OpenWrite(WritePlan plan, CancellationToken ct = default) => Inner.OpenWrite(plan, ct);

        private static async Task<Item> Inflate(Task<Item> stat)
        {
            var item = await stat;
            return item.Kind == ItemKind.File ? item with { Size = (item.Size ?? 0) + 1 } : item;
        }
    }
}
