using System.Text.Json;
using DigitalBrain.Spi;
using DigitalBrain.Tests.Unit.Neurons;
using DigitalBrain.UI;
using DigitalBrain.Units;

namespace DigitalBrain.Tests.Unit.Units;

[State("adv.counter")]
public sealed record AdvCounter(int Count = 0)
{
    public AdvCounter() : this(0) { }
}

[Signal("adv.bump")]
public sealed record AdvBump(int Amount = 1) : Signal;

[Signal("adv.noop")]
public sealed record AdvNoop : Signal;

[Signal("adv.fail")]
public sealed record AdvFail : Signal;

[Signal("adv.custom")]
public sealed record AdvCustom(string Tag) : Signal;

public static class AdvSynapses
{
    [Synapse("adv.bump", "adv.counter")]
    public static AdvCounter Bump(SignalEnvelope s, AdvCounter c) =>
        c with { Count = c.Count + (s.Payload.TryGetProperty("Amount", out var a) ? a.GetInt32() : 1) };

    [Synapse("adv.noop", "adv.counter")]
    public static AdvCounter Noop(SignalEnvelope s, AdvCounter c) => c;

    [Synapse("adv.fail", "adv.counter")]
    public static AdvCounter Fail(SignalEnvelope s, AdvCounter c) => throw new InvalidOperationException("boom from reaction");

    [Synapse("adv.counter")]
    public static View Show(AdvCounter c) =>
        new("adv.counter", new Row(
            [
                new Button("bump_btn", "Bump", new Emit("adv.counter", "adv.bump", JsonSerializer.SerializeToElement(new { Amount = 5 }))),
                new Button("foreign_btn", "Foreign", new Emit("malicious.owner", "adv.bump", JsonSerializer.SerializeToElement(new { Amount = 1 }))),
                new Text($"Count is {c.Count}")
            ]));
}

public sealed class UnitMachineAdversarialFacts
{
    private const string StateAlias = "adv.counter";
    private const string NeuronAddress = "alice/adv.counter/default";
    private readonly UnitMachine<AdvCounter> _machine = new(StateAlias, NeuronAddress);

    [Fact]
    public void NullStateTransition_DirectNullResult_ThrowsInvalidOperationException()
    {
        var step = _machine.Start(new StoredUnit<AdvCounter>(new AdvCounter(1), null));
        var cause = new AdvBump(1);

        var ex = Assert.Throws<InvalidOperationException>(() => _machine.Reacted(step, cause, null));
        Assert.Contains(StateAlias, ex.Message, StringComparison.Ordinal);
        Assert.Contains("null", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void NullStateTransition_ReactionWithNullNext_ThrowsInvalidOperationException()
    {
        var step = _machine.Start(new StoredUnit<AdvCounter>(new AdvCounter(1), null));
        var cause = new AdvBump(1);
        var badReaction = new Reaction<AdvCounter>(null!, []);

        var ex = Assert.Throws<InvalidOperationException>(() => _machine.Reacted(step, cause, badReaction));
        Assert.Contains("returned a Reaction with no next state", ex.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("a string")]
    [InlineData(12345)]
    [InlineData(3.14)]
    public void NullStateTransition_IncompatibleResultType_ThrowsInvalidOperationException(object invalid)
    {
        var step = _machine.Start(new StoredUnit<AdvCounter>(new AdvCounter(1), null));
        var cause = new AdvBump(1);

        var ex = Assert.Throws<InvalidOperationException>(() => _machine.Reacted(step, cause, invalid));
        Assert.Contains(invalid.GetType().FullName!, ex.Message, StringComparison.Ordinal);
        Assert.Contains(nameof(AdvCounter), ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void SilentStateChanged_StateReturn_GeneratesSingleStateChangedSignal()
    {
        var step = _machine.Start(new StoredUnit<AdvCounter>(new AdvCounter(0), null));
        var cause = new AdvBump(5);

        var nextStep = _machine.Reacted(step, cause, new AdvCounter(5));

        Assert.True(nextStep.Changed);
        Assert.Equal(5, nextStep.State.Count);
        var signal = Assert.Single(nextStep.Signals);
        var changed = Assert.IsType<StateChanged>(signal);
        Assert.Equal(NeuronAddress, changed.Neuron);
        Assert.Equal("adv.bump", changed.Cause);
    }

    [Fact]
    public void SilentStateChanged_ReactionWithEmptySignals_GeneratesStateChangedSignal()
    {
        var step = _machine.Start(new StoredUnit<AdvCounter>(new AdvCounter(0), null));
        var cause = new AdvBump(2);
        var reaction = Reaction<AdvCounter>.Of(new AdvCounter(2));

        var nextStep = _machine.Reacted(step, cause, reaction);

        Assert.True(nextStep.Changed);
        Assert.Equal(2, nextStep.State.Count);
        var signal = Assert.Single(nextStep.Signals);
        var changed = Assert.IsType<StateChanged>(signal);
        Assert.Equal("adv.bump", changed.Cause);
    }

    [Fact]
    public void SilentStateChanged_DeduplicatesAcrossMultipleReactionsInSameStep()
    {
        var step = _machine.Start(new StoredUnit<AdvCounter>(new AdvCounter(0), null));
        var cause = new AdvBump(1);

        var step1 = _machine.Reacted(step, cause, new AdvCounter(1));
        var step2 = _machine.Reacted(step1, cause, new AdvCounter(2));

        Assert.True(step2.Changed);
        Assert.Equal(2, step2.State.Count);
        var signal = Assert.Single(step2.Signals);
        Assert.IsType<StateChanged>(signal);
    }

    [Fact]
    public void CustomSignals_SuppressesSilentStateChanged()
    {
        var step = _machine.Start(new StoredUnit<AdvCounter>(new AdvCounter(0), null));
        var cause = new AdvBump(1);
        var custom = new AdvCustom("audit");
        var reaction = Reaction<AdvCounter>.Of(new AdvCounter(10), custom);

        var nextStep = _machine.Reacted(step, cause, reaction);

        Assert.True(nextStep.Changed);
        Assert.Equal(10, nextStep.State.Count);
        var signal = Assert.Single(nextStep.Signals);
        Assert.Same(custom, signal);
        Assert.DoesNotContain(nextStep.Signals, s => s is StateChanged);
    }

    [Fact]
    public void IdenticalReference_NoOpLeavesStepUnchanged()
    {
        var state = new AdvCounter(42);
        var step = _machine.Start(new StoredUnit<AdvCounter>(state, null));
        var cause = new AdvBump(0);

        var nextStep = _machine.Reacted(step, cause, state);

        Assert.False(nextStep.Changed);
        Assert.Same(step, nextStep);
        Assert.Same(state, nextStep.State);
        Assert.Empty(nextStep.Signals);
    }

    [Fact]
    public void ViewProjection_DeclaresByInEmit_IsRefusedAndNotStored()
    {
        var step = _machine.Start(new StoredUnit<AdvCounter>(new AdvCounter(1), null));
        var maliciousView = new View(StateAlias, new Button("btn", "Bump",
            new Emit(StateAlias, "adv.bump", JsonSerializer.SerializeToElement(new { by = "spoofed_user", amount = 1 }))));

        var (viewedStep, outcome) = _machine.Viewed(step, maliciousView);

        Assert.Equal(ViewOutcome.DeclaresBy, outcome);
        Assert.Null(viewedStep.View);
        Assert.Empty(viewedStep.Signals);
    }

    [Fact]
    public void ViewProjection_ValidView_IncrementsGenerationAndEmitsViewChanged()
    {
        var step = _machine.Start(new StoredUnit<AdvCounter>(new AdvCounter(1), null));
        var view1 = new View(StateAlias, new Text("v1"));

        var (step1, outcome1) = _machine.Viewed(step, view1);

        Assert.Equal(ViewOutcome.Stored, outcome1);
        Assert.NotNull(step1.View);
        Assert.Equal(1, step1.View.Generation);
        Assert.Equal(StateAlias, step1.View.Alias);
        var sig1 = Assert.Single(step1.Signals);
        var vc1 = Assert.IsType<ViewChanged>(sig1);
        Assert.Equal(1, vc1.Generation);

        var view2 = new View(StateAlias, new Text("v2"));
        var (step2, outcome2) = _machine.Viewed(step1, view2);

        Assert.Equal(ViewOutcome.Stored, outcome2);
        Assert.NotNull(step2.View);
        Assert.Equal(2, step2.View.Generation);
        Assert.Equal(2, step2.Signals.Count);
        var vc2 = Assert.IsType<ViewChanged>(step2.Signals[1]);
        Assert.Equal(2, vc2.Generation);
    }

    [Fact]
    public void ViewProjection_NotAView_ReturnsNotAViewAndStepUnchanged()
    {
        var step = _machine.Start(new StoredUnit<AdvCounter>(new AdvCounter(1), null));

        var (stepNull, outcomeNull) = _machine.Viewed(step, null);
        Assert.Equal(ViewOutcome.NotAView, outcomeNull);
        Assert.Same(step, stepNull);

        var (stepStr, outcomeStr) = _machine.Viewed(step, "not a view object");
        Assert.Equal(ViewOutcome.NotAView, outcomeStr);
        Assert.Same(step, stepStr);
    }

    [Fact]
    public void Press_StaleGeneration_ThrowsActivationRefusedException()
    {
        var wire = JsonSerializer.SerializeToElement(new
        {
            kind = "button",
            key = "btn",
            emit = new { owner = StateAlias, signal = "adv.bump" }
        });
        var published = new PublishedView(StateAlias, 5, wire);

        var ex = Assert.Throws<ActivationRefusedException>(() =>
            _machine.Press(published, StateAlias, generation: 4, "btn", null, "user1"));
        Assert.Equal(ActivationRefusedException.Stale, ex.Reason);
    }

    [Fact]
    public void Press_NullView_ThrowsActivationRefusedException()
    {
        var ex = Assert.Throws<ActivationRefusedException>(() =>
            _machine.Press(null, StateAlias, generation: 1, "btn", null, "user1"));
        Assert.Equal(ActivationRefusedException.Stale, ex.Reason);
    }

    [Fact]
    public void Press_MismatchedClaimedAlias_ThrowsActivationRefusedException()
    {
        var wire = JsonSerializer.SerializeToElement(new
        {
            kind = "button",
            key = "btn",
            emit = new { owner = StateAlias, signal = "adv.bump" }
        });
        var published = new PublishedView(StateAlias, 1, wire);

        var ex = Assert.Throws<ActivationRefusedException>(() =>
            _machine.Press(published, "wrong.alias", generation: 1, "btn", null, "user1"));
        Assert.Equal(ActivationRefusedException.Stale, ex.Reason);
    }

    [Fact]
    public void Press_UnknownNode_ThrowsActivationRefusedException()
    {
        var wire = JsonSerializer.SerializeToElement(new
        {
            kind = "button",
            key = "btn",
            emit = new { owner = StateAlias, signal = "adv.bump" }
        });
        var published = new PublishedView(StateAlias, 1, wire);

        var ex = Assert.Throws<ActivationRefusedException>(() =>
            _machine.Press(published, StateAlias, generation: 1, "non_existent_key", null, "user1"));
        Assert.Equal(ActivationRefusedException.UnknownNode, ex.Reason);
    }

    [Fact]
    public void Press_NodeWithoutEmit_ThrowsActivationRefusedException()
    {
        var wire = JsonSerializer.SerializeToElement(new
        {
            kind = "text",
            key = "label",
            text = "Just a label"
        });
        var published = new PublishedView(StateAlias, 1, wire);

        var ex = Assert.Throws<ActivationRefusedException>(() =>
            _machine.Press(published, StateAlias, generation: 1, "label", null, "user1"));
        Assert.Equal(ActivationRefusedException.NoEmit, ex.Reason);
    }

    [Fact]
    public void Press_ForeignOwner_ThrowsActivationRefusedException()
    {
        var wire = JsonSerializer.SerializeToElement(new
        {
            kind = "button",
            key = "btn",
            emit = new { owner = "foreign.owner", signal = "adv.bump" }
        });
        var published = new PublishedView(StateAlias, 1, wire);

        var ex = Assert.Throws<ActivationRefusedException>(() =>
            _machine.Press(published, StateAlias, generation: 1, "btn", null, "user1"));
        Assert.Equal(ActivationRefusedException.ForeignOwner, ex.Reason);
    }

    [Fact]
    public void Press_PayloadConstruction_StripsAuthorByAndAppendsActivatorByAndFieldValue()
    {
        var wire = JsonSerializer.SerializeToElement(new
        {
            kind = "button",
            key = "btn",
            emit = new { owner = StateAlias, signal = "adv.bump", args = new { by = "author_hack", count = 99 } }
        });
        var published = new PublishedView(StateAlias, 1, wire);

        var emitted = _machine.Press(published, StateAlias, 1, "btn", fieldValue: "typed_val", by: "real_activator");

        Assert.Equal("adv.bump", emitted.Alias);
        Assert.Equal(99, emitted.Payload.GetProperty("count").GetInt32());
        Assert.Equal("typed_val", emitted.Payload.GetProperty("value").GetString());
        Assert.Equal("real_activator", emitted.Payload.GetProperty("by").GetString());
    }

    [Fact]
    public async Task UnitNeuron_Activate_SingleCommitAtomicTurnWithoutReadback()
    {
        var neuronId = NeuronId.Parse(NeuronAddress);
        var context = new RecordingContext(neuronId);
        var catalog = new SynapseCatalogSource(SynapseCatalog.Scan([typeof(AdvCounter), typeof(AdvSynapses)]));
        var neuron = new UnitNeuron<AdvCounter>(catalog, new UnboundSlotResolver());
        NeuronAttachment.Attach(neuron, context);

        // Prime the neuron with an initial view
        var initialView = AdvSynapses.Show(new AdvCounter(0));
        var initialWire = JsonSerializer.SerializeToElement<View>(initialView, DigitalBrain.Invocation.RuntimeProtocol.Json);
        var publishedView = new PublishedView(StateAlias, 1, initialWire);
        context.Store(JsonSerializer.SerializeToUtf8Bytes(new StoredUnit<AdvCounter>(new AdvCounter(0), publishedView), DigitalBrain.Invocation.RuntimeProtocol.Json));
        await NeuronAttachment.Activate(neuron, TestContext.Current.CancellationToken);

        // Perform UI press
        await neuron.Activate(StateAlias, generation: 1, nodeKey: "bump_btn", fieldValue: null, by: "alice");

        // Assert: EXACTLY ONE commit entry written to journal!
        var write = Assert.Single(context.Writes);
        var savedState = Assert.IsType<AdvCounter>(write.State);
        Assert.Equal(5, savedState.Count);

        // Verify published view was also updated atomically to generation 2
        var currentView = await neuron.Read(StateAlias);
        Assert.NotNull(currentView);
        Assert.Equal(2, currentView.Generation);

        // Assert signals committed in that single entry:
        // [said (the EmittedSignal from Press), StateChanged, ViewChanged(gen 2)]
        Assert.Equal(3, write.Signals.Count);
        var said = Assert.IsType<EmittedSignal>(write.Signals[0]);
        Assert.Equal("adv.bump", said.Alias);
        Assert.Equal("alice", said.Payload.GetProperty("by").GetString());
        Assert.Equal(5, said.Payload.GetProperty("Amount").GetInt32());

        var stateChanged = Assert.IsType<StateChanged>(write.Signals[1]);
        Assert.Equal(NeuronAddress, stateChanged.Neuron);
        Assert.Equal("adv.bump", stateChanged.Cause);

        var viewChanged = Assert.IsType<ViewChanged>(write.Signals[2]);
        Assert.Equal(StateAlias, viewChanged.Alias);
        Assert.Equal(2, viewChanged.Generation);
    }

    [Fact]
    public async Task UnitNeuron_Activate_UnchangedState_OnlyPublishesSaidWithoutCommit()
    {
        var neuronId = NeuronId.Parse(NeuronAddress);
        var context = new RecordingContext(neuronId);
        var catalog = new SynapseCatalogSource(SynapseCatalog.Scan([typeof(AdvCounter), typeof(AdvSynapses)]));
        var neuron = new UnitNeuron<AdvCounter>(catalog, new UnboundSlotResolver());
        NeuronAttachment.Attach(neuron, context);

        var viewWire = JsonSerializer.SerializeToElement(new
        {
            kind = "button",
            key = "noop_btn",
            emit = new { owner = StateAlias, signal = "adv.noop" }
        });
        var publishedView = new PublishedView(StateAlias, 1, viewWire);
        context.Store(JsonSerializer.SerializeToUtf8Bytes(new StoredUnit<AdvCounter>(new AdvCounter(10), publishedView), DigitalBrain.Invocation.RuntimeProtocol.Json));
        await NeuronAttachment.Activate(neuron, TestContext.Current.CancellationToken);

        await neuron.Activate(StateAlias, generation: 1, nodeKey: "noop_btn", fieldValue: null, by: "alice");

        // Zero writes because state was unchanged!
        Assert.Empty(context.Writes);
        // Said was published ephemeral
        Assert.Contains(context.Published, p => p is EmittedSignal { Alias: "adv.noop" });
    }

    [Fact]
    public async Task UnitNeuron_Activate_FailingReaction_PublishesSaidBeforeRethrow()
    {
        var neuronId = NeuronId.Parse(NeuronAddress);
        var context = new RecordingContext(neuronId);
        var catalog = new SynapseCatalogSource(SynapseCatalog.Scan([typeof(AdvCounter), typeof(AdvSynapses)]));
        var neuron = new UnitNeuron<AdvCounter>(catalog, new UnboundSlotResolver());
        NeuronAttachment.Attach(neuron, context);

        var viewWire = JsonSerializer.SerializeToElement(new
        {
            kind = "button",
            key = "fail_btn",
            emit = new { owner = StateAlias, signal = "adv.fail" }
        });
        var publishedView = new PublishedView(StateAlias, 1, viewWire);
        context.Store(JsonSerializer.SerializeToUtf8Bytes(new StoredUnit<AdvCounter>(new AdvCounter(10), publishedView), DigitalBrain.Invocation.RuntimeProtocol.Json));
        await NeuronAttachment.Activate(neuron, TestContext.Current.CancellationToken);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            neuron.Activate(StateAlias, generation: 1, nodeKey: "fail_btn", fieldValue: null, by: "alice"));
        Assert.Equal("boom from reaction", ex.Message);

        // Zero writes
        Assert.Empty(context.Writes);
        // Said was published to preserve causation before rethrowing
        Assert.Contains(context.Published, p => p is EmittedSignal { Alias: "adv.fail" });
    }
}
