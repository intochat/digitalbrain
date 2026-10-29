using DigitalBrain.Capabilities;
using DigitalBrain.Capabilities.Storage;
using DigitalBrain.Operations;
using DigitalBrain.Testing.Conformance.Storage;

namespace DigitalBrain.Testing.Tests.Unit.Conformance;

public sealed class StorageFakeFacts
{
    [Fact]
    public async Task WithStorage_binds_fakes_as_storage_at_1_in_binding_order()
    {
        await using var os = TestOS.Local()
            .WithStorage("drive", new FakeStorage("Work Drive", Quota.Known(1, 2)))
            .WithStorage("c", label: "C:");

        var broker = (ICapabilityBroker)os.Services.GetService(typeof(ICapabilityBroker))!;
        var all = await broker.ResolveAll<IStorage>(new BrainId("ada"), TestContext.Current.CancellationToken);
        var labels = new List<string>();
        foreach (var connection in all)
        {
            labels.Add((await connection.Capability.Describe(TestContext.Current.CancellationToken)).Label);
        }

        Assert.Equal(["drive", "c"], all.Select(connection => connection.ConnectionKey));
        Assert.Equal(["Work Drive", "C:"], labels);
    }

    [Fact]
    public async Task FakeStorage_records_calls_and_seeds_files()
    {
        var fake = new FakeStorage("Mem");
        var file = fake.AddFile(fake.Root, "a.txt", "hi"u8.ToArray());

        Assert.Equal("hi", await fake.ReadText(file, TestContext.Current.CancellationToken));
        Assert.Contains(nameof(IStorage.Read), fake.Calls);
    }

    [Fact]
    public async Task InMemoryOperations_record_started_progress_and_done()
    {
        var storage = new InMemoryStorage();
        var id = await storage.Write(WritePlan.CreateFolder(storage.Root, "x"), TestContext.Current.CancellationToken);
        var result = await storage.Operations.WaitFor(id, TestContext.Current.CancellationToken);

        Assert.IsType<DigitalBrain.Operations.OperationResult.Succeeded>(result);
        Assert.Equal(["started", "progress", "done"], storage.Operations.Events.Where(e => e.Id == id).Select(e => e.Stage));
    }

    [Fact]
    public async Task Completion_publishes_the_terminal_event_before_releasing_waiters()
    {
        var operations = new InMemoryOperations();
        var operation = await operations.Start(WritePlan.CreateFolder(new ItemId("root"), "x"), TestContext.Current.CancellationToken);
        Task<(OperationResult Result, InMemoryOperationEvent[] Events)> observation;
        var previous = SynchronizationContext.Current;
        try
        {
            SynchronizationContext.SetSynchronizationContext(new ImmediateContext());
            observation = Observe(operations, operation.Id);
        }
        finally
        {
            SynchronizationContext.SetSynchronizationContext(previous);
        }

        var result = new OperationResult.Succeeded();
        await operation.Complete(result);
        var observed = await observation.WaitAsync(TestContext.Current.CancellationToken);

        Assert.Same(result, observed.Result);
        Assert.Same(result, Assert.Single(observed.Events, entry => entry.Stage == "done").Result);
    }

    [Fact]
    public async Task Concurrent_completion_and_cancellation_publish_exactly_one_matching_terminal_event()
    {
        var ct = TestContext.Current.CancellationToken;
        var operations = new InMemoryOperations();
        var operation = await operations.Start(WritePlan.CreateFolder(new ItemId("root"), "x"), ct);
        var ready = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var completions = Enumerable.Range(0, 16).Select(index => Task.Run(async () =>
        {
            await ready.Task.WaitAsync(ct);
            if (index % 2 == 0)
            {
                await operation.Complete(new OperationResult.Succeeded());
            }
            else
            {
                await operations.Cancel(operation.Id, ct);
            }
            var terminal = Assert.Single(operations.Events, entry => entry.Stage == "done");
            Assert.Same(terminal.Result, await operations.WaitFor(operation.Id, ct));
        }, ct)).ToArray();

        ready.SetResult();
        await Task.WhenAll(completions);

        var result = await operations.WaitFor(operation.Id, ct);
        Assert.Same(result, Assert.Single(operations.Events, entry => entry.Stage == "done").Result);
    }

    private static async Task<(OperationResult Result, InMemoryOperationEvent[] Events)> Observe(InMemoryOperations operations, OperationId id)
    {
        var result = await operations.WaitFor(id, CancellationToken.None).ConfigureAwait(true);
        return (result, [.. operations.Events]);
    }

    private sealed class ImmediateContext : SynchronizationContext
    {
        public override void Post(SendOrPostCallback callback, object? state) => callback(state);
    }
}
