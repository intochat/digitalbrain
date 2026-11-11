using DigitalBrain.Spi;
using DigitalBrain.Tests.Unit.Neurons;
using DigitalBrain.Units;

namespace DigitalBrain.Tests.Unit.Synapses;

[State("slotted")]
public sealed record Slotted(int Value)
{
    public Slotted() : this(0) { }
}

[Signal("test.slot-poke")]
public sealed record SlotPoke : Signal;

public static class SlotFunctions
{
    [Synapse(typeof(SlotPoke), "slotted")]
    public static async Task<Slotted> Read(SlotPoke s, [Slot] ISqlRead db, Slotted state, CancellationToken ct) =>
        state with { Value = await db.Scalar<int>("select 7", null, ct) };
}

public sealed class FakeSqlRead(int value) : ISqlRead
{
    public Task<T> Scalar<T>(string sql, object? args, CancellationToken ct) => Task.FromResult((T)(object)value);
}

public sealed class FakeSlotResolver(Dictionary<string, object> bound) : ISlotResolver
{
    public int Calls { get; private set; }
    public NeuronId? LastState { get; private set; }

    public Task<object> Resolve(NeuronId state, SlotDescriptor slot, CancellationToken ct)
    {
        Calls++;
        LastState = state;
        return bound.TryGetValue(slot.Name, out var value) ? Task.FromResult(value) : throw new SlotUnboundException(state.Type, slot.Name);
    }
}

public sealed class StateNeuronSlotFacts
{
    private static readonly NeuronId Id = new(new BrainId("alice"), "slotted", "default");

    private RecordingContext Context { get; } = new(Id);

    private UnitNeuron<Slotted> Attach(Type function, ISlotResolver resolver)
    {
        var neuron = new UnitNeuron<Slotted>(new SynapseCatalogSource(SynapseCatalog.Scan([typeof(Slotted), function])), resolver);
        NeuronAttachment.Attach(neuron, Context);
        return neuron;
    }

    [Fact]
    public async Task AReactionReceivesTheResolvedSlot()
    {
        var resolver = new FakeSlotResolver(new() { ["db"] = new FakeSqlRead(7) });
        var n = Attach(typeof(SlotFunctions), resolver);

        await n.Receive(new SlotPoke(), TestContext.Current.CancellationToken);

        Assert.Equal(7, ((Slotted)Context.Writes[0].State).Value);
        Assert.Equal(1, resolver.Calls);
    }

    [Fact]
    public async Task AReactionWithAnUnboundSlotFailsAndSavesNothing()
    {
        var n = Attach(typeof(SlotFunctions), new FakeSlotResolver([]));

        await Assert.ThrowsAsync<SlotUnboundException>(() => n.Receive(new SlotPoke(), TestContext.Current.CancellationToken));

        Assert.Empty(Context.Writes);
        Assert.Contains(Context.Published, p => p is SynapseFinished { Outcome: "failed" } f && f.Function.EndsWith(".Read", StringComparison.Ordinal));
    }
}
