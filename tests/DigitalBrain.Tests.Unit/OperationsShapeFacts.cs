using DigitalBrain.Operations;
using DigitalBrain.Spi;

namespace DigitalBrain.Tests.Unit;

public sealed class OperationsShapeFacts
{
    private sealed record CopyPlan(string From, string To, bool DryRun) : OperationPlan(DryRun);

    private sealed class Handle(OperationId id) : IOperation, IDisposable
    {
        public CancellationTokenSource Source { get; } = new();
        public List<double> Progress { get; } = [];
        public OperationResult? Result { get; private set; }
        public OperationId Id { get; } = id;
        public CancellationToken Cancellation => Source.Token;
        public ValueTask Report(double fraction) { Progress.Add(fraction); return ValueTask.CompletedTask; }
        public ValueTask Complete(OperationResult result) { Result = result; return ValueTask.CompletedTask; }
        public void Dispose() => Source.Dispose();
    }

    private sealed class Operations : IOperations
    {
        public Dictionary<OperationId, Handle> Started { get; } = [];

        public Task<IOperation> Start(OperationPlan plan, CancellationToken ct)
        {
            var handle = new Handle(new OperationId($"op-{Started.Count + 1}"));
            Started[handle.Id] = handle;
            return Task.FromResult<IOperation>(handle);
        }

        public Task Cancel(OperationId id, CancellationToken ct) => Started[id].Source.CancelAsync();
    }

    [Fact]
    public async Task Start_returns_a_handle_with_progress_completion_and_cancellation()
    {
        var operations = new Operations();
        var ct = TestContext.Current.CancellationToken;
        var operation = await operations.Start(new CopyPlan("a", "b", DryRun: false), ct);
        Assert.False(string.IsNullOrEmpty(operation.Id.Value));

        await operation.Report(0.5);
        await operation.Complete(new OperationResult.Succeeded());
        var handle = operations.Started[operation.Id];
        Assert.Equal([0.5], handle.Progress);
        Assert.IsType<OperationResult.Succeeded>(handle.Result);

        Assert.False(operation.Cancellation.IsCancellationRequested);
        await operations.Cancel(operation.Id, ct);
        Assert.True(operation.Cancellation.IsCancellationRequested);
        handle.Dispose();
    }

    [Fact]
    public void Results_cover_succeeded_failed_cancelled_and_planned()
    {
        OperationResult[] results =
        [
            new OperationResult.Succeeded(),
            new OperationResult.Failed("conflict", "b exists"),
            new OperationResult.Cancelled(),
            new OperationResult.Planned(["copy a to b"]),
        ];
        Assert.Equal(4, results.Select(result => result.GetType()).Distinct().Count());
        Assert.True(new CopyPlan("a", "b", DryRun: true).DryRun);
    }
}
