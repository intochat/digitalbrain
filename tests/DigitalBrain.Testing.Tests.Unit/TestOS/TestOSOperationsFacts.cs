using DigitalBrain.Capabilities.Storage;
using DigitalBrain.Contracts;
using DigitalBrain.Operations;
using OS = DigitalBrain.Testing.TestOS;

namespace DigitalBrain.Testing.Tests.Unit.Hosting;

[NeuronAlias("test.op-writer")]
public interface IOpWriter
{
    Task<string> Run(WriteRequest request);
}

public sealed record WriteRequest(string Kind, string? Source, string? Folder, string? Name, string? Text, ConflictPolicy Conflict, bool DryRun);

/// <summary>Starts a write plan through its own context and returns the result kind and code.</summary>
[Neuron("test.op-writer")]
public sealed class OpWriterNeuron : Neuron, IOpWriter
{
    public async Task<string> Run(WriteRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        var plan = request.Kind switch
        {
            "create-folder" => WritePlan.CreateFolder(new ItemId(request.Folder!), request.Name!, request.Conflict),
            "write-file" => WritePlan.WriteFile(new ItemId(request.Folder!), request.Name!, System.Text.Encoding.UTF8.GetBytes(request.Text ?? string.Empty), request.Conflict),
            _ => WritePlan.Move(new ItemId(request.Source!), new ItemId(request.Folder!), request.Conflict),
        } with
        { DryRun = request.DryRun };
        var operation = (FakeOperation)await Context.Operations.Start(plan, CancellationToken.None);
        var result = await operation.Completion;
        return result switch
        {
            OperationResult.Failed failed => "failed:" + failed.Code,
            OperationResult.Planned planned => "planned:" + planned.Steps.Count,
            _ => result.GetType().Name.ToLowerInvariant(),
        };
    }
}

// 05-11: TestOS Context.Operations has the runtime operation semantics (shared StorageWriteRunner, operation signals in order).
public sealed class TestOSOperationsFacts : IAsyncLifetime
{
    private static readonly BrainId Brain = new("ada");
    private readonly FakeStorage _a = new("A");
    private readonly FakeStorage _b = new("B");
    private readonly OS _os;

    public TestOSOperationsFacts()
    {
        _os = OS.Local().WithStorage("a", _a).WithStorage("b", _b);
        _os.Register(typeof(OpWriterNeuron).Assembly);
    }

    public ValueTask InitializeAsync() => ValueTask.CompletedTask;

    public ValueTask DisposeAsync() => _os.DisposeAsync();

    private IOpWriter Writer => _os.Get<IOpWriter>(new NeuronId(Brain, "test.op-writer", "w"));

    private async Task<List<Signal>> Signals()
        => [.. (await _os.Journal(Brain).Read(new NeuronId(Brain, "test.op-writer", "w"), 0, SignalPage.MaxLimit, TestContext.Current.CancellationToken)).Signals.Select(item => item.Signal)];

    [Fact]
    public async Task A_write_publishes_started_progress_and_done_in_order_and_changes_the_connection()
    {
        Assert.Equal("succeeded", await Writer.Run(new("create-folder", null, _a.Root.Value, "New", null, ConflictPolicy.Fail, false)));

        var signals = await Signals();
        Assert.IsType<OperationStarted>(signals[0]);
        Assert.Contains(signals, signal => signal is OperationProgress);
        Assert.IsType<OperationResult.Succeeded>(Assert.IsType<OperationDone>(signals[^1]).Result);
        Assert.Contains((await _a.List(_a.Root, new ListQuery(), TestContext.Current.CancellationToken)).Items, item => item.Name == "New");
    }

    [Fact]
    public async Task Dry_run_and_fail_by_default_conflicts_match_the_runtime()
    {
        var file = _a.AddFile(_a.Root, "x.txt", "original"u8.ToArray());

        Assert.Equal("planned:1", await Writer.Run(new("write-file", null, _a.Root.Value, "y.txt", "y", ConflictPolicy.Fail, true)));
        Assert.Equal("failed:conflict", await Writer.Run(new("write-file", null, _a.Root.Value, "x.txt", "new", ConflictPolicy.Fail, false)));
        Assert.Equal("original", await _a.ReadText(file, TestContext.Current.CancellationToken));
        Assert.Equal("succeeded", await Writer.Run(new("write-file", null, _a.Root.Value, "x.txt", "second", ConflictPolicy.Rename, false)));
        Assert.Contains((await _a.List(_a.Root, new ListQuery(), TestContext.Current.CancellationToken)).Items, item => item.Name == "x (1).txt");
    }

    [Fact]
    public async Task A_move_between_two_bound_connections_copies_verifies_and_trashes_the_source()
    {
        var file = _a.AddFile(_a.Root, "doc.txt", "hello"u8.ToArray());

        // 07-02: a move between connections is destructive, so the default policy asks a person first.
        var moving = Writer.Run(new("move", file.Value, _b.Root.Value, null, null, ConflictPolicy.Fail, false));
        while (_os.Confirmations.Waiting.Count == 0) { await Task.Delay(10, TestContext.Current.CancellationToken); }
        await _os.Confirmations.Approve(_os.Confirmations.Waiting[0].PlanId);
        Assert.Equal("succeeded", await moving);

        var moved = (await _b.List(_b.Root, new ListQuery(), TestContext.Current.CancellationToken)).Items.Single(item => item.Name == "doc.txt");
        Assert.Equal("hello", await _b.ReadText(moved.Id, TestContext.Current.CancellationToken));
        Assert.Empty((await _a.List(_a.Root, new ListQuery(), TestContext.Current.CancellationToken)).Items);
    }
}
