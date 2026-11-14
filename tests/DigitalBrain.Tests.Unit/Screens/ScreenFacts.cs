using System.Text.Json;
using DigitalBrain.Contracts;
using DigitalBrain.Kernel;
using DigitalBrain.Limits;
using DigitalBrain.Neurons;
using DigitalBrain.Tests.Unit.Neurons;
using DigitalBrain.UI;
using DigitalBrain.Units;
using Xunit;

namespace DigitalBrain.Tests.Unit.Screens;

public sealed record SampleCounterState
{
    public int Count { get; init; }
    public string LastBy { get; init; } = "";
}

[Signal("counter.increment")]
public sealed record Increment(int Amount = 1) : Signal;

[Signal("counter.set-caller")]
public sealed record SetCaller(string Value, string? By = null) : Signal;

[Neuron("app.sample-counter")]
public sealed class SampleCounterScreen : Screen<SampleCounterState>
{
    protected override View Render(SampleCounterState state)
    {
        using var emptyObj = JsonDocument.Parse("""{"value": ""}""");
        return new View(
            Title: "Sample Counter",
            Root: new Column([
                new Heading($"Count: {state.Count}"),
                new Button("btn-inc", "Add One", Emits(new Increment(1))),
                new Button("btn-alien", "Alien Button", new Emit("alien.screen", "alien.signal")),
                new Button("btn-no-emit", "No Emit Button", Intent: new Intent(IntentKind.Copy, "nothing")),
                new Field("fld-input", "input-label", FieldKind.Text, state.LastBy, OnSubmit: new Emit(Alias, "counter.set-caller", emptyObj.RootElement.Clone()))
            ])
        );
    }

    [On]
    public Task When(Increment signal) => Show(State with { Count = State.Count + signal.Amount });

    [On]
    public Task When(SetCaller signal) => Show(State with { LastBy = signal.By ?? "" });

    // Expose protected methods for testing
    public Task TestShow(SampleCounterState next, params Signal[] facts) => Show(next, facts);
    public Emit TestEmits<TSignal>(TSignal signal) where TSignal : Signal => Emits(signal);
    public T TestGet<T>(string key) where T : class => Get<T>(key);
    public Task TestFollow<TSource, TSignal>(string key) where TSource : class where TSignal : Signal => Follow<TSource, TSignal>(key);
}

public sealed class InvalidByScreen : Screen<SampleCounterState>
{
    protected override View Render(SampleCounterState state)
    {
        using var jsonDoc = JsonDocument.Parse("""{"by": "attacker"}""");
        return new View(
            Title: "Invalid",
            Root: new Column([
                new Button("btn-hack", "Hack", new Emit(Alias, "counter.increment", jsonDoc.RootElement.Clone()))
            ])
        );
    }

    [On]
    public Task When(Increment signal) => Show(State with { Count = State.Count + signal.Amount });
}

public sealed class HugeViewScreen : Screen<SampleCounterState>
{
    protected override View Render(SampleCounterState state)
    {
        var hugeText = new string('A', UnitLimits.MaxStateBytes + 1024);
        return new View(Title: "Huge", Root: new Heading(hugeText));
    }

    [On]
    public Task When(Increment signal) => Show(State with { Count = State.Count + 1 });
}

public sealed class ScreenFacts
{
    private static async Task<(TScreen Screen, RecordingContext Context)> Attached<TScreen>(string alias = "app.sample-counter") where TScreen : Neuron, new()
    {
        var screen = new TScreen();
        var id = new NeuronId(new BrainId("alice"), alias, "default");
        var context = new RecordingContext(id);
        NeuronAttachment.Attach(screen, context);
        await NeuronAttachment.Activate(screen, CancellationToken.None);
        return (screen, context);
    }

    [Fact]
    public async Task OnActivate_StoresInitialView_AtGenerationOne()
    {
        var (screen, _) = await Attached<SampleCounterScreen>();
        var view = await screen.Read();

        Assert.NotNull(view);
        Assert.Equal("app.sample-counter", view.Alias);
        Assert.Equal(1, view.Generation);
        Assert.Contains("Count: 0", view.View.GetRawText());
    }

    [Fact]
    public async Task Request_ExecutesReaction_IncrementsGenerationAndEmitsStateAndViews()
    {
        var (screen, _) = await Attached<SampleCounterScreen>();

        await screen.Request<Increment>(new Increment(5));

        var view = await screen.Read();
        Assert.NotNull(view);
        Assert.Equal(2, view.Generation);
        Assert.Contains("Count: 5", view.View.GetRawText());
    }

    [Fact]
    public async Task StoreState_IdenticalView_PreservesGeneration_WithoutViewChanged()
    {
        var (screen, _) = await Attached<SampleCounterScreen>();
        var view1 = await screen.Read();
        Assert.NotNull(view1);
        Assert.Equal(1, view1.Generation);

        // Add 0 keeps view text identical
        await screen.Request<Increment>(new Increment(0));

        var view2 = await screen.Read();
        Assert.NotNull(view2);
        Assert.Equal(1, view2.Generation);
    }

    [Fact]
    public async Task Activate_ValidInteraction_ExecutesReaction()
    {
        var (screen, _) = await Attached<SampleCounterScreen>();
        var initialView = await screen.Read();
        Assert.NotNull(initialView);

        await screen.Activate("app.sample-counter", initialView.Generation, "btn-inc");

        var updatedView = await screen.Read();
        Assert.NotNull(updatedView);
        Assert.Equal(2, updatedView.Generation);
        Assert.Contains("Count: 1", updatedView.View.GetRawText());
    }

    [Fact]
    public async Task Activate_WithHostAssertedBy_InjectsAuthenticatedCaller()
    {
        var (screen, _) = await Attached<SampleCounterScreen>();
        var initialView = await screen.Read();
        Assert.NotNull(initialView);

        await screen.Activate("app.sample-counter", initialView.Generation, "fld-input", fieldValue: "ignored", by: "alice@company.com");

        var updatedView = await screen.Read();
        Assert.NotNull(updatedView);
        Assert.Contains("alice@company.com", updatedView.View.GetRawText());
    }

    [Fact]
    public async Task Activate_StaleGeneration_ThrowsActivationRefusedException()
    {
        var (screen, _) = await Attached<SampleCounterScreen>();
        var view = await screen.Read();
        Assert.NotNull(view);

        var ex = await Assert.ThrowsAsync<ActivationRefusedException>(() =>
            screen.Activate("app.sample-counter", view.Generation + 999, "btn-inc"));
        Assert.Equal(ActivationRefusedException.Stale, ex.Reason);
    }

    [Fact]
    public async Task Activate_UnknownNode_ThrowsActivationRefusedException()
    {
        var (screen, _) = await Attached<SampleCounterScreen>();
        var view = await screen.Read();
        Assert.NotNull(view);

        var ex = await Assert.ThrowsAsync<ActivationRefusedException>(() =>
            screen.Activate("app.sample-counter", view.Generation, "nonexistent-button"));
        Assert.Equal(ActivationRefusedException.UnknownNode, ex.Reason);
    }

    [Fact]
    public async Task Activate_NoEmitNode_ThrowsActivationRefusedException()
    {
        var (screen, _) = await Attached<SampleCounterScreen>();
        var view = await screen.Read();
        Assert.NotNull(view);

        var ex = await Assert.ThrowsAsync<ActivationRefusedException>(() =>
            screen.Activate("app.sample-counter", view.Generation, "btn-no-emit"));
        Assert.Equal(ActivationRefusedException.NoEmit, ex.Reason);
    }

    [Fact]
    public async Task Activate_ForeignOwner_ThrowsActivationRefusedException()
    {
        var (screen, _) = await Attached<SampleCounterScreen>();
        var view = await screen.Read();
        Assert.NotNull(view);

        var ex = await Assert.ThrowsAsync<ActivationRefusedException>(() =>
            screen.Activate("app.sample-counter", view.Generation, "btn-alien"));
        Assert.Equal(ActivationRefusedException.ForeignOwner, ex.Reason);
    }

    [Fact]
    public async Task StoreState_ViewDeclaringBy_ThrowsInvalidOperationException()
    {
        var invalid = new InvalidByScreen();
        var context = new RecordingContext(new NeuronId(new BrainId("alice"), "InvalidByScreen", "default"));
        NeuronAttachment.Attach(invalid, context);
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => NeuronAttachment.Activate(invalid, CancellationToken.None));
        Assert.Contains("declares the host-reserved 'by'", ex.Message);
    }

    [Fact]
    public async Task StoreState_ExceedingMaxStateBytes_ThrowsInvalidOperationException()
    {
        var huge = new HugeViewScreen();
        var context = new RecordingContext(new NeuronId(new BrainId("alice"), "HugeViewScreen", "default"));
        NeuronAttachment.Attach(huge, context);
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => NeuronAttachment.Activate(huge, CancellationToken.None));
        Assert.Contains("exceeds the", ex.Message);
    }

    [Fact]
    public async Task Read_MismatchedAlias_ReturnsNull()
    {
        var (screen, _) = await Attached<SampleCounterScreen>();

        var mismatched = await screen.Read("wrong-alias");
        Assert.Null(mismatched);
    }

    [Fact]
    public async Task Request_EmptySignalOrMissingReaction_ThrowsArgumentException()
    {
        var (screen, _) = await Attached<SampleCounterScreen>();

        await Assert.ThrowsAsync<ArgumentException>(() => screen.Request(""));
        await Assert.ThrowsAsync<ArgumentException>(() => screen.Request("nonexistent.signal"));
    }

    [Fact]
    public async Task StoreState_PersistsDomainStateWithoutPublishedViewJsonBloat_AndReadsLegacyEnvelope()
    {
        var (screen, context) = await Attached<SampleCounterScreen>();
        await screen.Request<Increment>(new Increment(7));

        // Verify persisted state is SampleCounterState directly, NOT StoredState<SampleCounterState> with PublishedView JSON
        var latestWrite = context.Writes[^1].State;
        var savedState = Assert.IsType<SampleCounterState>(latestWrite);
        Assert.Equal(7, savedState.Count);

        var serializedBytes = JsonSerializer.SerializeToUtf8Bytes(savedState, DigitalBrain.Invocation.RuntimeProtocol.OptionsFor(typeof(SampleCounterState)));
        var serializedJson = System.Text.Encoding.UTF8.GetString(serializedBytes);
        Assert.DoesNotContain("Sample Counter", serializedJson);
        Assert.DoesNotContain("generation", serializedJson, StringComparison.OrdinalIgnoreCase);

        // Reloading from raw TState UTF-8 bytes restores state and renders view in memory without extra journal writes
        var reloaded = new SampleCounterScreen();
        var reloadContext = new RecordingContext(new NeuronId(new BrainId("alice"), "app.sample-counter", "default"));
        reloadContext.Store(serializedBytes);
        NeuronAttachment.Attach(reloaded, reloadContext);
        await NeuronAttachment.Activate(reloaded, CancellationToken.None);

        Assert.Empty(reloadContext.Writes);
        var reloadedView = await reloaded.Read();
        Assert.NotNull(reloadedView);
        Assert.Equal(1, reloadedView.Generation);
        Assert.Contains("Count: 7", reloadedView.View.GetRawText());

        // Legacy StoredState<SampleCounterState> JSON bytes also load backward-compatibly
        var legacyEnvelopeBytes = JsonSerializer.SerializeToUtf8Bytes(
            new StoredState<SampleCounterState>(new SampleCounterState { Count = 42, LastBy = "legacy" }, reloadedView),
            DigitalBrain.Invocation.RuntimeProtocol.OptionsFor(typeof(StoredState<SampleCounterState>)));
        var legacyScreen = new SampleCounterScreen();
        var legacyContext = new RecordingContext(new NeuronId(new BrainId("alice"), "app.sample-counter", "default"));
        legacyContext.Store(legacyEnvelopeBytes);
        NeuronAttachment.Attach(legacyScreen, legacyContext);
        await NeuronAttachment.Activate(legacyScreen, CancellationToken.None);

        var legacyLoadedView = await legacyScreen.Read();
        Assert.NotNull(legacyLoadedView);
        await legacyScreen.Request<Increment>(new Increment(1));
        var updatedAfterLegacy = await legacyScreen.Read();
        Assert.NotNull(updatedAfterLegacy);
        Assert.Contains("Count: 43", updatedAfterLegacy.View.GetRawText());
    }
}
