using System.Text;
using DigitalBrain.Capabilities;
using DigitalBrain.Capabilities.Storage;
using DigitalBrain.Contracts;
using DigitalBrain.Operations;
using DigitalBrain.Runtime.Orleans.Capabilities;
using DigitalBrain.Runtime.Orleans.Capabilities.Storage;
using DigitalBrain.Runtime.Orleans.Hosting;
using DigitalBrain.Runtime.Orleans.Operations;
using DigitalBrain.Runtime.Orleans.Testing;
using DigitalBrain.Runtime.Orleans.Tests.Integration.Hosting;
using DigitalBrain.Spi;
using DigitalBrain.Testing.Conformance.Storage;
using Microsoft.Extensions.DependencyInjection;
using Orleans.Hosting;

using OrleansSdk = Orleans;
namespace DigitalBrain.Runtime.Orleans.Tests.Integration.Operations;

/// <summary>Two in-memory storage connections, <c>a</c> and <c>b</c>, for every brain; <c>b</c> can be slowed down.</summary>
public sealed class OperationConnections : ICapabilityConnections
{
    public InMemoryStorage A { get; } = new("a");

    public SlowStorage B { get; } = new(new InMemoryStorage("b"));

    public Task<IReadOnlyList<BrainConnection>> ListInOrder(BrainId brain, CancellationToken ct)
        => Task.FromResult<IReadOnlyList<BrainConnection>>(
        [
            new BrainConnection("a", "A", true, [Capability.Storage.V1], null),
            new BrainConnection("b", "B", true, [Capability.Storage.V1], null),
        ]);

    public Task<TCapability> Bind<TCapability>(BrainId brain, string key, string callerApp, CancellationToken ct) where TCapability : class
        => Task.FromResult((TCapability)(object)(key == "a" ? A : B));

    public Task RequestConnection(BrainId brain, CapabilityId capability, string callerApp, CancellationToken ct) => Task.CompletedTask;

    /// <summary>The result of a write an in-memory connection started (its own operations).</summary>
    public static Task<OperationResult> Result(IStorage storage, OperationId id, CancellationToken ct)
    {
        while (storage is RecordingStorage recording) { storage = recording.Inner; }
        if (storage is SlowStorage slow) { storage = slow.Inner; }
        return ((InMemoryStorage)storage).Operations.WaitFor(id, ct);
    }
}

/// <summary>An in-memory connection whose streaming writes wait <see cref="Delay"/> first.</summary>
public sealed class SlowStorage(InMemoryStorage inner) : IStorage
{
    public InMemoryStorage Inner { get; } = inner;

    public TimeSpan Delay { get; set; }

    public Task<StorageDescriptor> Describe(CancellationToken ct = default) => Inner.Describe(ct);

    public Task<IReadOnlyList<Item>> Roots(CancellationToken ct = default) => Inner.Roots(ct);

    public Task<Page<Item>> List(ItemId folder, ListQuery query, CancellationToken ct = default) => Inner.List(folder, query, ct);

    public Task<Item> Stat(ItemId id, CancellationToken ct = default) => Inner.Stat(id, ct);

    public Task<Content> Read(ItemId id, ByteRange? range, CancellationToken ct = default) => Inner.Read(id, range, ct);

    public Task<OperationId> Write(WritePlan plan, CancellationToken ct = default) => Inner.Write(plan, ct);

    public Task<Page<Item>> Search(SearchQuery query, CancellationToken ct = default) => Inner.Search(query, ct);

    public Task<IReadOnlyList<Item>> Ancestors(ItemId id, CancellationToken ct = default) => Inner.Ancestors(id, ct);

    public Task<StorageReadStream> OpenRead(ItemId item, ByteRange? range, CancellationToken ct = default) => Inner.OpenRead(item, range, ct);

    public async Task<StorageWriteStream> OpenWrite(WritePlan plan, CancellationToken ct = default)
    {
        if (Delay > TimeSpan.Zero) { await Task.Delay(Delay, ct); }
        return await Inner.OpenWrite(plan, ct);
    }
}

/// <summary>Refuses plans whose folder name starts with <c>Denied</c>.</summary>
public sealed class NamedGate : IOperationGate
{
    public TaskCompletionSource<OperationGateDecision>? Waiting { get; set; }
    public Task<OperationGateDecision> Check(OperationRequest request, CancellationToken ct)
        => request.Plan is WritePlan { Name: "Waiting-for-human" } && Waiting is { } pending ? pending.Task.WaitAsync(ct)
            : Task.FromResult(request.Plan is WritePlan { Name: { } name } && name.StartsWith("Denied", StringComparison.Ordinal)
            ? OperationGateDecision.Deny("refused by the fact gate")
            : OperationGateDecision.Allow);
}

[NeuronAlias("test.writer")]
public interface IWriter
{
    Task<string> CreateFolder(string parent, string name);
}

/// <summary>A hosted neuron that starts an operation through its own context.</summary>
[Neuron("probe.writer")]
public sealed class WriterNeuron : Neuron, IWriter
{
    public async Task<string> CreateFolder(string parent, string name)
        => (await Context.Operations.Start(WritePlan.CreateFolder(new ItemId(parent), name), CancellationToken.None)).Id.Value;
}

/// <summary>A silo over BrainLog with the two connections, the in-memory result reader and the fact gate.</summary>
public sealed class OperationSilo : IAsyncLifetime
{
    internal const string DriverLifetime = "operation-silo-process";
    private readonly TempHome _home = new();
    private IInProcessHost? _host;

    public OperationConnections Connections { get; } = new();
    public NamedGate Gate { get; } = new();
    internal bool? DriverEnded { get; set; }

    public IServiceProvider Services => (_host ?? throw new InvalidOperationException("Not started.")).Services;

    public Task Restart(CancellationToken ct) => _host!.Restart(ct);

    internal RuntimeOperations Operations(string brain) => new(Services, new BrainId(brain), "fact");

    internal IOperationEntity Entity(string brain, OperationId id) => RuntimeOperations.Entity(Services, new BrainId(brain), id);

    /// <summary>The signals the entity of <paramref name="id"/> journaled, in order.</summary>
    internal async Task<List<Signal>> Signals(string brain, OperationId id, CancellationToken ct)
    {
        var history = Services.GetRequiredService<BrainSignalHistories>().For(new BrainId(brain));
        var page = await history.Read(new NeuronId(new BrainId(brain), OperationNeuron.NeuronType, id.Value), 0, SignalPage.MaxLimit, ct);
        return [.. page.Signals.Select(item => item.Signal)];
    }

    public async ValueTask InitializeAsync()
        => _host = await new LocalSiloHostFactory(new Stores(), services =>
        {
            services.AddSingleton<ICapabilityConnections>(Connections);
            services.AddSingleton<StorageWriteResult>(OperationConnections.Result);
            services.AddSingleton<IOperationGate>(Gate);
            services.AddSingleton<IOperationDriverLifetime>(new Lifetime(this));
        }).Start(new InProcessHostOptions { Home = _home.Path, Modules = [typeof(HostingModule)] }, CancellationToken.None);

    public async ValueTask DisposeAsync()
    {
        if (_host is not null) { await _host.DisposeAsync(); }
        _home.Dispose();
    }

    private sealed class Stores : IClusterStoreRegistration
    {
        public void Register(ISiloBuilder silo) => silo.AddBrainStorage(typeof(HostingModule).Assembly);
    }

    private sealed class Lifetime(OperationSilo owner) : IOperationDriverLifetime
    {
        public string? Capture() => DriverLifetime;
        public bool? HasEnded(string token) => token == DriverLifetime ? owner.DriverEnded : null;
    }
}

// D-04, CAP-04: the runtime operation entity on a silo over BrainLog.
public sealed class OperationEntityFacts(OperationSilo silo) : IClassFixture<OperationSilo>
{
    [Fact]
    public async Task StartReturnsACancellableHandleWhileHumanApprovalIsPending()
    {
        var ct = TestContext.Current.CancellationToken;
        silo.Gate.Waiting = new(TaskCreationOptions.RunContinuationsAsynchronously);
        var operations = silo.Operations("op-wait-human");
        try
        {
            var operation = await operations.Start(WritePlan.CreateFolder(silo.Connections.A.Root, "Waiting-for-human"), ct).WaitAsync(TimeSpan.FromSeconds(2), ct);
            Assert.Equal(OperationSilo.DriverLifetime, (await silo.Entity("op-wait-human", operation.Id).Get())!.DriverLifetime);
            Assert.False(((RuntimeOperation)operation).Completion.IsCompleted);
            await operations.Cancel(operation.Id, ct);
            Assert.IsType<OperationResult.Cancelled>(await Done(operation));
            Assert.DoesNotContain(await silo.Connections.A.Children(silo.Connections.A.Root), item => item.Name == "Waiting-for-human");
        }
        finally
        {
            silo.Gate.Waiting.TrySetResult(OperationGateDecision.Deny("test finished"));
            silo.Gate.Waiting = null;
        }
    }

    private static byte[] Bytes(string text) => Encoding.UTF8.GetBytes(text);

    private static async Task<OperationResult> Done(IOperation operation)
        => await ((RuntimeOperation)operation).Completion.WaitAsync(TimeSpan.FromSeconds(30));

    [Fact]
    public async Task Start_saves_the_entity_and_publishes_started_progress_and_done_in_order()
    {
        var ct = TestContext.Current.CancellationToken;
        var a = silo.Connections.A;
        var operation = await silo.Operations("op-order").Start(WritePlan.CreateFolder(a.Root, "New-order"), ct);

        Assert.IsType<OperationResult.Succeeded>(await Done(operation));
        var signals = await silo.Signals("op-order", operation.Id, ct);
        Assert.IsType<OperationStarted>(signals[0]);
        Assert.Contains(signals, signal => signal is OperationProgress);
        Assert.IsType<OperationDone>(signals[^1]);
        Assert.Single(signals.OfType<OperationDone>());
        Assert.All(signals.OfType<OperationProgress>(), progress => Assert.True(signals.IndexOf(progress) is > 0 && signals.IndexOf(progress) < signals.Count - 1));
        Assert.IsType<OperationResult.Succeeded>(Assert.IsType<OperationDone>(signals[^1]).Result);

        var state = (await silo.Entity("op-order", operation.Id).Get())!;
        Assert.Equal(OperationPhase.Done, state.Phase);
        Assert.Equal("fact", state.Origin);
        Assert.Equal(nameof(WritePlan), state.Kind);
        Assert.NotNull((await a.Children(a.Root)).SingleOrDefault(item => item.Name == "New-order"));
    }

    [Fact]
    public async Task A_dry_run_ends_planned_and_changes_nothing()
    {
        var ct = TestContext.Current.CancellationToken;
        var a = silo.Connections.A;
        var before = (await a.Children(a.Root)).Count;
        var operation = await silo.Operations("op-dry").Start(WritePlan.CreateFolder(a.Root, "Dry") with { DryRun = true }, ct);

        Assert.Single(Assert.IsType<OperationResult.Planned>(await Done(operation)).Steps);
        Assert.Equal(before, (await a.Children(a.Root)).Count);
    }

    [Fact]
    public async Task A_conflict_fails_by_default_and_overwrite_replaces_only_when_explicit()
    {
        var ct = TestContext.Current.CancellationToken;
        var a = silo.Connections.A;
        var folder = a.AddFolder(a.Root, "conflicts");
        var file = a.AddFile(folder, "x.txt", Bytes("original"));
        var operations = silo.Operations("op-conflict");

        var failed = await Done(await operations.Start(WritePlan.WriteFile(folder, "x.txt", Bytes("new")), ct));
        Assert.Equal("conflict", Assert.IsType<OperationResult.Failed>(failed).Code);
        Assert.Equal("original", await a.ReadText(file, ct));

        var renamed = await Done(await operations.Start(WritePlan.WriteFile(folder, "x.txt", Bytes("second"), ConflictPolicy.Rename), ct));
        Assert.IsType<OperationResult.Succeeded>(renamed);
        Assert.Contains(await a.Children(folder), item => item.Name == "x (1).txt");

        var overwritten = await Done(await operations.Start(WritePlan.WriteFile(folder, "x.txt", Bytes("third"), ConflictPolicy.Overwrite), ct));
        Assert.IsType<OperationResult.Succeeded>(overwritten);
        Assert.Equal("third", await a.ReadText((await a.Children(folder)).Single(item => item.Name == "x.txt").Id, ct));
    }

    [Fact]
    public async Task Inline_bytes_are_kept_out_of_the_persisted_plan_and_the_started_signal()
    {
        var ct = TestContext.Current.CancellationToken;
        var a = silo.Connections.A;
        var operation = await silo.Operations("op-inline").Start(WritePlan.WriteFile(a.Root, "secret-inline.txt", Bytes("top secret")), ct);
        await Done(operation);

        var state = (await silo.Entity("op-inline", operation.Id).Get())!;
        Assert.Equal(10, state.InlineLength);
        Assert.StartsWith("sha256:", state.InlineHash, StringComparison.Ordinal);
        Assert.DoesNotContain(Convert.ToBase64String(Bytes("top secret")), state.PlanJson, StringComparison.Ordinal);
        Assert.Null(Assert.IsType<OperationStarted>((await silo.Signals("op-inline", operation.Id, ct))[0]).Plan.Inline);
    }

    [Fact]
    public async Task Cancel_mid_way_ends_cancelled_and_no_progress_is_saved_after_the_cancel()
    {
        var ct = TestContext.Current.CancellationToken;
        var a = silo.Connections.A;
        var b = silo.Connections.B;
        var folder = a.AddFolder(a.Root, "hundred");
        for (var i = 0; i < 100; i++) { a.AddFile(folder, $"f{i:D3}.txt", Bytes($"file {i}")); }
        b.Delay = TimeSpan.FromMilliseconds(20);
        try
        {
            var operations = silo.Operations("op-cancel");
            var operation = await operations.Start(WritePlan.Copy(folder, b.Inner.Root), ct);
            while ((await silo.Signals("op-cancel", operation.Id, ct)).OfType<OperationProgress>().Count() < 2) { await Task.Delay(50, ct); }

            await operations.Cancel(operation.Id, ct);
            var progressAtCancel = (await silo.Signals("op-cancel", operation.Id, ct)).OfType<OperationProgress>().Count();

            Assert.IsType<OperationResult.Cancelled>(await Done(operation));
            var signals = await silo.Signals("op-cancel", operation.Id, ct);
            Assert.Equal(progressAtCancel, signals.OfType<OperationProgress>().Count());
            Assert.IsType<OperationDone>(signals[^1]);
            Assert.DoesNotContain(await b.Inner.Children(b.Inner.Root), item => item.Name == "hundred");
            Assert.Equal(100, (await a.Children(folder)).Count);
        }
        finally { b.Delay = TimeSpan.Zero; }
    }

    [Fact]
    public async Task A_cross_connection_move_through_the_entity_copies_verifies_and_trashes_the_source()
    {
        var ct = TestContext.Current.CancellationToken;
        var a = silo.Connections.A;
        var b = silo.Connections.B.Inner;
        var file = a.AddFile(a.Root, "moved.txt", Bytes("moving"));
        var operation = await silo.Operations("op-move").Start(WritePlan.Move(file, b.Root), ct);

        Assert.IsType<OperationResult.Succeeded>(await Done(operation));
        Assert.Equal("moving", await b.ReadText((await b.Children(b.Root)).Single(item => item.Name == "moved.txt").Id, ct));
        Assert.DoesNotContain(await a.Children(a.Root), item => item.Name == "moved.txt");
    }

    [Fact]
    public async Task The_gate_is_asked_and_a_refusal_ends_failed_denied_with_nothing_written()
    {
        var ct = TestContext.Current.CancellationToken;
        var a = silo.Connections.A;
        var operation = await silo.Operations("op-gate").Start(WritePlan.CreateFolder(a.Root, "Denied-folder"), ct);

        Assert.Equal("denied", Assert.IsType<OperationResult.Failed>(await Done(operation)).Code);
        Assert.DoesNotContain(await a.Children(a.Root), item => item.Name == "Denied-folder");
    }

    [Fact]
    public async Task A_hosted_neuron_starts_operations_through_its_context_with_itself_as_origin()
    {
        var ct = TestContext.Current.CancellationToken;
        var a = silo.Connections.A;
        var writer = NeuronHosting.Get<IWriter>(silo.Services, NeuronId.Parse("op-ctx/probe.writer/w1"));
        var id = new OperationId(await writer.CreateFolder(a.Root.Value, "From-context"));

        var result = await RuntimeOperations.WaitFor(silo.Services, new BrainId("op-ctx"), id, ct).WaitAsync(TimeSpan.FromSeconds(30), ct);
        Assert.IsType<OperationResult.Succeeded>(result);
        Assert.Equal("op-ctx/probe.writer/w1", (await silo.Entity("op-ctx", id).Get())!.Origin);
        Assert.Contains(await a.Children(a.Root), item => item.Name == "From-context");
    }
}

// The entity is persisted: a done operation reads back after a silo restart and is never run again.
public sealed class OperationRestartFacts(OperationSilo silo) : IClassFixture<OperationSilo>
{
    [Fact]
    public async Task APendingApprovalIsInterruptedAfterTheDriverEndsAndTheEndpointChanges()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var restarting = new OperationSilo();
        await restarting.InitializeAsync();
        restarting.Gate.Waiting = new(TaskCreationOptions.RunContinuationsAsynchronously);
        var brain = "op-new-endpoint";
        try
        {
            var operation = await restarting.Operations(brain).Start(
                WritePlan.CreateFolder(restarting.Connections.A.Root, "Waiting-for-human"), ct);
            var before = (await restarting.Entity(brain, operation.Id).Get())!;
            Assert.Equal(OperationPhase.Running, before.Phase);
            Assert.Equal(OperationSilo.DriverLifetime, before.DriverLifetime);

            await restarting.Restart(ct);
            var address = restarting.Services.GetRequiredService<OrleansSdk.Runtime.ILocalSiloDetails>().SiloAddress;
            Assert.True(OrleansSdk.Runtime.SiloAddress.TryParse(before.Driver!, out var previousAddress));
            Assert.NotEqual(previousAddress.Endpoint, address.Endpoint);
            // The host adapter supplies this proof; membership alone cannot recognize the changed endpoint.
            restarting.DriverEnded = true;
            await restarting.Entity(brain, operation.Id).Reconcile();
            await restarting.Entity(brain, operation.Id).Reconcile();

            var after = (await restarting.Entity(brain, operation.Id).Get())!;
            Assert.Equal("interrupted", Assert.IsType<OperationResult.Failed>(after.Result).Code);
            Assert.Empty(await restarting.Connections.A.Children(restarting.Connections.A.Root));
            Assert.Single((await restarting.Signals(brain, operation.Id, ct)).OfType<OperationDone>());
        }
        finally { restarting.Gate.Waiting.TrySetResult(OperationGateDecision.Deny("test finished")); }
    }

    [Fact]
    public async Task A_done_operation_survives_a_silo_restart_and_is_not_run_again_on_reactivation()
    {
        var ct = TestContext.Current.CancellationToken;
        var a = silo.Connections.A;
        var operation = await silo.Operations("op-restart").Start(WritePlan.CreateFolder(a.Root, "Kept"), ct);
        Assert.IsType<OperationResult.Succeeded>(await ((RuntimeOperation)operation).Completion.WaitAsync(TimeSpan.FromSeconds(30), ct));
        var signalsBefore = (await silo.Signals("op-restart", operation.Id, ct)).Count;

        await silo.Restart(ct);

        var state = (await silo.Entity("op-restart", operation.Id).Get())!;
        Assert.Equal(OperationPhase.Done, state.Phase);
        Assert.IsType<OperationResult.Succeeded>(state.Result);
        Assert.Equal("op-restart", state.Brain);
        await Task.Delay(500, ct);
        Assert.Single(await a.Children(a.Root), item => item.Name == "Kept");
        Assert.Equal(signalsBefore, (await silo.Signals("op-restart", operation.Id, ct)).Count);
        Assert.IsType<OperationResult.Succeeded>(await RuntimeOperations.WaitFor(silo.Services, new BrainId("op-restart"), operation.Id, ct));
    }
}

internal static class OperationTrees
{
    public static async Task<List<Item>> Children(this IStorage storage, ItemId folder)
        => [.. (await storage.List(folder, new ListQuery(null, ListQuery.MaxPageSize))).Items];
}
