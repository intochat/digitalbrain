using DigitalBrain.Operations;
using Xunit;

namespace DigitalBrain.Tests.Unit.Operations;

public sealed class OperationHandlerFacts
{
    private sealed record TestPlan(string Data, bool DryRun = false) : OperationPlan(DryRun);

    private sealed record CustomPayloadPlan(ReadOnlyMemory<byte>? Inline, bool DryRun = false) : OperationPlan(DryRun)
    {
        public override ReadOnlyMemory<byte>? Payload => Inline;
        public override OperationPlan Persisted() => this with { Inline = null };
        public override OperationPlan WithPayload(ReadOnlyMemory<byte> payload) => this with { Inline = payload };
    }

    private sealed class TestPlanHandler : IOperationHandler<TestPlan>
    {
        public Task<OperationApproval?> Describe(TestPlan plan, OperationScope scope, CancellationToken ct) =>
            Task.FromResult<OperationApproval?>(null);

        public Task<OperationResult> Run(TestPlan plan, IOperationNeuron operation, OperationScope scope, CancellationToken ct) =>
            Task.FromResult<OperationResult>(new OperationResult.Succeeded());
    }

    [Fact]
    public void OperationHandlersRegistersAndResolvesByTypeAndKind()
    {
        var handlers = new OperationHandlers();
        var handler = new TestPlanHandler();
        handlers.Register(handler);

        var byType = handlers.Find(typeof(TestPlan));
        Assert.NotNull(byType);

        var byKind = handlers.Find(nameof(TestPlan));
        Assert.NotNull(byKind);
        Assert.Same(byType, byKind);

        Assert.Null(handlers.Find(typeof(CustomPayloadPlan)));
        Assert.Null(handlers.Find("UnknownPlan"));
    }

    [Fact]
    public void OperationPlanDefaultVirtualPayloadMembersBehaveCorrectly()
    {
        var plan = new TestPlan("hello");
        Assert.Null(plan.Payload);
        Assert.Same(plan, plan.Persisted());
        Assert.Same(plan, plan.WithPayload("abc"u8.ToArray()));
    }

    [Fact]
    public void CustomPayloadPlanStripsAndReattachesPayload()
    {
        var bytes = "payload"u8.ToArray();
        var plan = new CustomPayloadPlan(bytes);

        Assert.NotNull(plan.Payload);
        Assert.Equal(bytes, plan.Payload.Value.ToArray());

        var persisted = plan.Persisted();
        Assert.Null(persisted.Payload);

        var restored = persisted.WithPayload(bytes);
        Assert.NotNull(restored.Payload);
        Assert.Equal(bytes, restored.Payload.Value.ToArray());
    }

    [Fact]
    public async Task OperationWaitForCompletesWhenResultIsSet()
    {
        var query = new FakeQuery();
        var id = new OperationId("op-wait");

        var waitTask = OperationWait.For(query, id, CancellationToken.None);
        Assert.False(waitTask.IsCompleted);

        query.SetView(new OperationView(id, "TestPlan", "origin", 1d, new OperationResult.Succeeded(), DateTimeOffset.UtcNow, null));

        var view = await waitTask;
        Assert.NotNull(view.Result);
        Assert.IsType<OperationResult.Succeeded>(view.Result);
    }

    [Fact]
    public async Task OperationWaitForObservesCancellation()
    {
        var query = new FakeQuery();
        var id = new OperationId("op-cancel");
        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(50));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => OperationWait.For(query, id, cts.Token));
    }

    private sealed class FakeQuery : IOperationsQuery
    {
        private OperationView? _view;

        public void SetView(OperationView view) => _view = view;

        public Task<OperationView?> Read(OperationId id, CancellationToken ct) => Task.FromResult(_view);
    }
}
