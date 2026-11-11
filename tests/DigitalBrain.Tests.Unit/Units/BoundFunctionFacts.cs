using DigitalBrain.Units;

namespace DigitalBrain.Tests.Unit.Units;

[State("test.sample")]
public sealed record SampleState(int Total = 0)
{
    public SampleState() : this(0) { }
}

[Signal("test.add")]
public sealed record AddSignal(int Amount) : Signal;

[Capability("test.multiplier", 1)]
public interface ISampleSlot
{
    Task<int> GetMultiplier();
}

public static class SampleSynapseFunctions
{
    [Synapse(typeof(AddSignal), "test.sample")]
    public static SampleState SyncAdd(AddSignal signal, SampleState state) =>
        state with { Total = state.Total + signal.Amount };

    [Synapse(typeof(AddSignal), "test.sample")]
    public static async Task<SampleState> AsyncAdd(AddSignal signal, SampleState state, CancellationToken ct)
    {
        await Task.Yield();
        ct.ThrowIfCancellationRequested();
        return state with { Total = state.Total + signal.Amount * 10 };
    }

    [Synapse(typeof(AddSignal), "test.sample")]
    public static SampleState Throwing(AddSignal signal, SampleState state) =>
        throw new InvalidOperationException("boom inside synapse");

    [Synapse(typeof(AddSignal), "test.sample")]
    public static async Task<SampleState> SlotAdd(AddSignal signal, SampleState state, [Slot] ISampleSlot slot)
    {
        var mult = await slot.GetMultiplier();
        return state with { Total = state.Total + signal.Amount * mult };
    }
}

public sealed class BoundFunctionFacts
{
    private static readonly NeuronId UnitId = new(new BrainId("alice"), "test.sample", "default");

    private sealed class FakeResolver(object instance) : ISlotResolver
    {
        public Task<object> Resolve(NeuronId unit, SlotDescriptor slot, CancellationToken ct) =>
            Task.FromResult(instance);
    }

    private sealed class FakeMultiplier(int factor) : ISampleSlot
    {
        public Task<int> GetMultiplier() => Task.FromResult(factor);
    }

    private static SynapseCatalog Catalog => SynapseCatalog.Scan([typeof(SampleState), typeof(SampleSynapseFunctions)]);

    [Fact]
    public async Task InvokeSynchronousFunctionReturnsMutatedState()
    {
        var desc = Catalog.For("test.sample").First(d => d.Function.Name == nameof(SampleSynapseFunctions.SyncAdd));
        var bound = new BoundFunction(desc);

        var call = new UnitCall(new AddSignal(5), null, null);
        var result = await bound.Invoke(call, new SampleState(10), new UnboundSlotResolver(), UnitId, CancellationToken.None);

        var next = Assert.IsType<SampleState>(result);
        Assert.Equal(15, next.Total);
    }

    [Fact]
    public async Task InvokeAsyncFunctionAwaitsAndReturnsResult()
    {
        var desc = Catalog.For("test.sample").First(d => d.Function.Name == nameof(SampleSynapseFunctions.AsyncAdd));
        var bound = new BoundFunction(desc);

        var call = new UnitCall(new AddSignal(3), null, null);
        var result = await bound.Invoke(call, new SampleState(10), new UnboundSlotResolver(), UnitId, CancellationToken.None);

        var next = Assert.IsType<SampleState>(result);
        Assert.Equal(40, next.Total);
    }

    [Fact]
    public async Task InvokeThrowingFunctionUnwrapsTargetInvocationException()
    {
        var desc = Catalog.For("test.sample").First(d => d.Function.Name == nameof(SampleSynapseFunctions.Throwing));
        var bound = new BoundFunction(desc);

        var call = new UnitCall(new AddSignal(1), null, null);
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => bound.Invoke(call, new SampleState(0), new UnboundSlotResolver(), UnitId, CancellationToken.None));

        Assert.Equal("boom inside synapse", ex.Message);
    }

    [Fact]
    public async Task InvokeWithSlotResolvesSlotAndExecutes()
    {
        var desc = Catalog.For("test.sample").First(d => d.Function.Name == nameof(SampleSynapseFunctions.SlotAdd));
        var bound = new BoundFunction(desc);

        var resolver = new FakeResolver(new FakeMultiplier(4));
        var call = new UnitCall(new AddSignal(5), null, null);
        var result = await bound.Invoke(call, new SampleState(2), resolver, UnitId, CancellationToken.None);

        var next = Assert.IsType<SampleState>(result);
        Assert.Equal(22, next.Total); // 2 + 5 * 4
    }
}
