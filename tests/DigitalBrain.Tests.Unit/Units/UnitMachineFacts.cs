using System.Text.Json;
using DigitalBrain.UI;
using DigitalBrain.Units;

namespace DigitalBrain.Tests.Unit.Units;

public sealed record MachineCounter(int Count = 0)
{
    public MachineCounter() : this(0) { }
}

[Signal("test.bump")]
public sealed record BumpSignal(int Amount) : Signal;

public sealed class UnitMachineFacts
{
    private readonly UnitMachine<MachineCounter> _machine = new("counter", "alice/counter/default");

    [Fact]
    public void StartInitializesFromStoredUnitOrCreatesNew()
    {
        var emptyStored = new StoredUnit<MachineCounter>(null, null);
        var initialStep = _machine.Start(emptyStored);

        Assert.NotNull(initialStep.State);
        Assert.Equal(0, initialStep.State.Count);
        Assert.Null(initialStep.View);
        Assert.False(initialStep.Changed);
        Assert.Empty(initialStep.Signals);

        var existingState = new MachineCounter(42);
        var view = new PublishedView("counter", 1, JsonSerializer.SerializeToElement(new { }));
        var existingStored = new StoredUnit<MachineCounter>(existingState, view);
        var step = _machine.Start(existingStored);

        Assert.Equal(42, step.State.Count);
        Assert.Equal(view, step.View);
        Assert.False(step.Changed);
    }

    [Fact]
    public void ReactedWithMutatedStateMarksChangedAndAppendsStateChanged()
    {
        var step = _machine.Start(new StoredUnit<MachineCounter>(new MachineCounter(5), null));
        var cause = new BumpSignal(3);

        var nextState = new MachineCounter(8);
        var nextStep = _machine.Reacted(step, cause, nextState);

        Assert.True(nextStep.Changed);
        Assert.Equal(8, nextStep.State.Count);
        var signal = Assert.Single(nextStep.Signals);
        var changed = Assert.IsType<StateChanged>(signal);
        Assert.Equal("test.bump", changed.Cause);
    }

    [Fact]
    public void ReactedWithSameReferenceKeepsChangedFalse()
    {
        var state = new MachineCounter(5);
        var step = _machine.Start(new StoredUnit<MachineCounter>(state, null));

        var nextStep = _machine.Reacted(step, new BumpSignal(0), state);

        Assert.False(nextStep.Changed);
        Assert.Same(step, nextStep);
        Assert.Empty(nextStep.Signals);
    }

    [Fact]
    public void ReactedWithReactionRecordAppendsCustomSignals()
    {
        var step = _machine.Start(new StoredUnit<MachineCounter>(new MachineCounter(1), null));
        var customSignal = new BumpSignal(99);
        var reaction = Reaction<MachineCounter>.Of(new MachineCounter(2), customSignal);

        var nextStep = _machine.Reacted(step, new BumpSignal(1), reaction);

        Assert.True(nextStep.Changed);
        Assert.Equal(2, nextStep.State.Count);
        var signal = Assert.Single(nextStep.Signals);
        Assert.Same(customSignal, signal);
    }

    [Fact]
    public void ReactedWithInvalidResultTypeThrowsInvalidOperationException()
    {
        var step = _machine.Start(new StoredUnit<MachineCounter>(new MachineCounter(1), null));

        Assert.Throws<InvalidOperationException>(() => _machine.Reacted(step, new BumpSignal(1), "invalid string"));
    }

    [Fact]
    public void ViewedWithValidViewIncrementsGenerationAndAppendsViewChanged()
    {
        var step = _machine.Start(new StoredUnit<MachineCounter>(new MachineCounter(1), null));
        var view = new View("counter", new Text("Count: 1"));

        var (viewedStep, outcome) = _machine.Viewed(step, view);

        Assert.Equal(ViewOutcome.Stored, outcome);
        Assert.NotNull(viewedStep.View);
        Assert.Equal(1, viewedStep.View.Generation);
        Assert.Equal("counter", viewedStep.View.Alias);
        var signal = Assert.Single(viewedStep.Signals);
        var viewChanged = Assert.IsType<ViewChanged>(signal);
        Assert.Equal(1, viewChanged.Generation);
        Assert.Equal("counter", viewChanged.Alias);
    }

    [Fact]
    public void ViewedWithNonViewReturnsNotAView()
    {
        var step = _machine.Start(new StoredUnit<MachineCounter>(new MachineCounter(1), null));

        var (viewedStep, outcome) = _machine.Viewed(step, "not a view");

        Assert.Equal(ViewOutcome.NotAView, outcome);
        Assert.Same(step, viewedStep);
    }

    [Fact]
    public void PressValidatesStaleGenerationAndForeignOwner()
    {
        var element = JsonSerializer.SerializeToElement(new
        {
            kind = "row",
            children = new object[]
            {
                new { key = "btn", kind = "button", emit = new { owner = "counter", signal = "test.bump", args = new { amount = 5 } } },
                new { key = "foreign_btn", kind = "button", emit = new { owner = "other", signal = "test.bump" } },
                new { key = "plain_btn", kind = "button" }
            }
        });

        var published = new PublishedView("counter", 2, element);

        // Stale generation
        Assert.Throws<ActivationRefusedException>(() => _machine.Press(published, "counter", 1, "btn", null, null));

        // Wrong alias
        Assert.Throws<ActivationRefusedException>(() => _machine.Press(published, "wrong", 2, "btn", null, null));

        // Unknown node
        Assert.Throws<ActivationRefusedException>(() => _machine.Press(published, "counter", 2, "unknown", null, null));

        // No emit
        Assert.Throws<ActivationRefusedException>(() => _machine.Press(published, "counter", 2, "plain_btn", null, null));

        // Foreign owner
        Assert.Throws<ActivationRefusedException>(() => _machine.Press(published, "counter", 2, "foreign_btn", null, null));

        // Valid press
        var pressed = _machine.Press(published, "counter", 2, "btn", null, "user_1");
        Assert.Equal("test.bump", pressed.Alias);
        Assert.True(pressed.Payload.TryGetProperty("amount", out var amount));
        Assert.Equal(5, amount.GetInt32());
        Assert.True(pressed.Payload.TryGetProperty("by", out var by));
        Assert.Equal("user_1", by.GetString());
    }
}
