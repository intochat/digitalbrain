using System.Text.Json;
using DigitalBrain.Spi;
using DigitalBrain.Tests.Unit.Neurons;

namespace DigitalBrain.Tests.Unit.Synapses;

[State("asker")]
public sealed record Asker(int Got = 0, int Asked = 0)
{
    public Asker() : this(0, 0) { }
}

// Not "Counted": DigitalBrain.Tests.Unit.Neurons already has a signal of that name (PlainNeuronFacts.cs:11).
[Signal("test.asker.counted")]
public sealed record AskerCounted(string Key) : Signal;

public static class AskerFunctions
{
    [Synapse(typeof(SlotPoke), "asker")]
    public static Reaction<Asker> Poll(SlotPoke s, Asker a, [Slot] Asks<IProbe> feed) =>
        Reaction<Asker>.Of(a with { Asked = a.Asked + 1 }).Ask(feed.Call("Get", "k", JsonElement.Parse("""{"id":"1"}""")));
}

public static class AskerSameState
{
    [Synapse(typeof(SlotPoke), "asker")]
    public static Reaction<Asker> Poll(SlotPoke s, Asker a, [Slot] Asks<IProbe> feed) =>
        Reaction<Asker>.Of(a).Ask(feed.Call("Get", "k", JsonElement.Parse("""{"id":"1"}""")));
}

public static class AskerForging
{
    [Synapse(typeof(SlotPoke), "asker")]
    public static Reaction<Asker> Poll(SlotPoke s, Asker a, [Slot] Asks<IProbe> feed) =>
        Reaction<Asker>.Of(a).Ask(new Asks<IProbe>("other").Call("Get", "k", JsonElement.Parse("{}")));
}

public sealed class StateNeuronAskFacts
{
    private static readonly NeuronId Id = new(new BrainId("alice"), "asker", "default");

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private RecordingContext Context { get; } = new(Id);

    private StateNeuron<Asker> Attach(Type function, ISlotResolver resolver)
    {
        var neuron = new StateNeuron<Asker>(new SynapseCatalogSource(SynapseCatalog.Scan([typeof(Asker), function])), resolver);
        NeuronAttachment.Attach(neuron, Context);
        return neuron;
    }

    [Fact]
    public async Task AReactionWithAsksSavesStateAndUnitAskedInOneWrite()
    {
        var resolver = new FakeSlotResolver(new());
        var n = Attach(typeof(AskerFunctions), resolver);

        await n.Receive(new SlotPoke(), Ct);

        var write = Assert.Single(Context.Writes);
        Assert.Equal(1, ((Asker)write.State).Asked);
        Assert.IsType<StateChanged>(write.Signals[0]);
        var asked = Assert.IsType<Asked>(write.Signals[1]);
        Assert.Equal(("asker", 32), (asked.Unit, asked.Batch.Length));
        Assert.Equal(("feed", "Get", "k"), (asked.Asks[0].Slot, asked.Asks[0].Operation, Assert.Single(asked.Asks).Key));
        Assert.Equal(0, resolver.Calls);
    }

    [Fact]
    public async Task AReactionThatReturnsTheSameStateAndOneAskStillJournalsUnitAsked()
    {
        var n = Attach(typeof(AskerSameState), new FakeSlotResolver(new()));

        await n.Receive(new SlotPoke(), Ct);

        var write = Assert.Single(Context.Writes);
        Assert.Equal(0, ((Asker)write.State).Asked);
        var asked = Assert.IsType<Asked>(write.Signals[1]);
        Assert.Equal("k", Assert.Single(asked.Asks).Key);
    }

    [Fact]
    public async Task AnAskOnASlotTheUnitDoesNotDeclareSavesNothingAndIsRecordedAsFailed()
    {
        var n = Attach(typeof(AskerForging), new FakeSlotResolver(new()));

        var e = await Assert.ThrowsAsync<InvalidOperationException>(() => n.Receive(new SlotPoke(), Ct));

        Assert.Contains("does not declare", e.Message, StringComparison.Ordinal);
        Assert.Empty(Context.Writes);
        Assert.Single(Context.Published.OfType<SynapseFailed>());
    }
}
