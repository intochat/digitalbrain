using DigitalBrain.Kernel;
using System.Text.Json;
using DigitalBrain.Neurons;
using DigitalBrain.Tests.Unit.Neurons;
using DigitalBrain.Units;

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

    [Synapse(typeof(Answered), "asker")]
    public static Reaction<Asker> Got(Answered all, Answer one, Asker a) =>
        one.Key == "bad" ? throw new InvalidOperationException("bad answer")
        : one.Key == "skip" ? Reaction<Asker>.Of(a)
        : one.Key == "more" ? Reaction<Asker>.Of(a with { Got = a.Got + 1 }).Ask(new Asks<IProbe>("feed").Call("Get", "next-" + a.Got, JsonElement.Parse("{}")))
        : one.Ok ? Reaction<Asker>.Of(a with { Got = a.Got + 1 }, new AskerCounted(one.Key))
        : Reaction<Asker>.Of(a);
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
    private static readonly NeuronId Mine = new(Id.Brain, "unit.asks", "asker");

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private RecordingContext Context { get; } = new(Id);

    private static JournaledSignal Group(NeuronId publisher, params (string Key, AnswerStatus Status)[] answers) =>
        new(publisher, 9, new Answered("asker", [.. answers.Select(a => new Answer("feed", "Get", a.Key, a.Status, AskRules.NoResult, null, 0))]), DateTimeOffset.UnixEpoch);

    private UnitNeuron<Asker> Attach(Type function, ISlotResolver resolver)
    {
        var neuron = new UnitNeuron<Asker>(new SynapseCatalogSource(SynapseCatalog.Scan([typeof(Asker), function])), resolver);
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

    [Fact]
    public async Task AnAnsweredFromTheUnitsAsksNeuronFoldsEveryAnswerAndSavesOnce()
    {
        var n = Attach(typeof(AskerFunctions), new FakeSlotResolver(new()));
        await n.Receive(Group(Mine, ("a", AnswerStatus.Ok), ("b", AnswerStatus.Failed), ("c", AnswerStatus.Ok)), Ct);

        var write = Assert.Single(Context.Writes);
        Assert.Equal(2, ((Asker)write.State).Got);
        Assert.Equal(["a", "c"], write.Signals.OfType<AskerCounted>().Select(c => c.Key));
    }

    [Fact]
    public async Task AnAnsweredFromAnyOtherPublisherRunsNothing()
    {
        var n = Attach(typeof(AskerFunctions), new FakeSlotResolver(new()));
        await n.Receive(Group(new NeuronId(Id.Brain, "other", "default"), ("a", AnswerStatus.Ok)), Ct);
        await n.Receive(Group(new NeuronId(Id.Brain, "unit.asks", "other"), ("a", AnswerStatus.Ok)), Ct);
        await n.Receive(new Answered("asker", [new Answer("feed", "Get", "a", AnswerStatus.Ok, default, null, 0)]), Ct);

        Assert.Empty(Context.Writes);
        Assert.DoesNotContain(Context.Published, p => p is SynapseStarted);
    }

    [Fact]
    public async Task AFailingAnswerFailsTheWholeGroup()
    {
        var n = Attach(typeof(AskerFunctions), new FakeSlotResolver(new()));
        await Assert.ThrowsAsync<InvalidOperationException>(() => n.Receive(Group(Mine, ("a", AnswerStatus.Ok), ("bad", AnswerStatus.Ok), ("c", AnswerStatus.Ok)), Ct));

        Assert.Empty(Context.Writes);
        var failed = Assert.Single(Context.Published.OfType<SynapseFailed>());
        Assert.Equal(("unit.answered", "bad answer"), (failed.Cause, failed.Message));
    }

    [Fact]
    public async Task AFoldThatChangesNothingSavesNothing()
    {
        var n = Attach(typeof(AskerFunctions), new FakeSlotResolver(new()));
        await n.Receive(Group(Mine, ("skip", AnswerStatus.Ok), ("skip", AnswerStatus.Ok)), Ct);

        Assert.Empty(Context.Writes);
        Assert.Contains(Context.Published, p => p is SynapseFinished { Outcome: "unchanged" });
    }

    [Fact]
    public async Task AsksReturnedWhileFoldingRideTheOneSave()
    {
        var n = Attach(typeof(AskerFunctions), new FakeSlotResolver(new()));
        await n.Receive(Group(Mine, ("more", AnswerStatus.Ok), ("more", AnswerStatus.Ok)), Ct);

        var asked = Assert.Single(Assert.Single(Context.Writes).Signals.OfType<Asked>());
        Assert.Equal(["next-0", "next-1"], asked.Asks.Select(a => a.Key));
    }
}
