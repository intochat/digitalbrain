using System.Reflection.Emit;
using System.Runtime.CompilerServices;
using System.Runtime.Loader;
using System.Text.Json;
using DigitalBrain.Contracts;
using DigitalBrain.Kernel;
using DigitalBrain.Neurons;
using DigitalBrain.Tests.Unit.Neurons;
using DigitalBrain.UI;
using DigitalBrain.Units;
using Xunit;

namespace DigitalBrain.Tests.Unit.Screens;

public sealed record ChallengerScreenState
{
    public int Counter { get; init; }
    public string Text { get; init; } = "";
}

[Signal("challenger.tick")]
public sealed record ChallengerTick(int Delta = 1) : Signal;

[Signal("challenger.set-text")]
public sealed record ChallengerSetText(string Value = "") : Signal;

[Neuron("challenger.screen")]
public sealed class ChallengerScreen : Screen<ChallengerScreenState>
{
    public ChallengerScreenState CurrentState => State;

    protected override View Render(ChallengerScreenState state)
    {
        return new View(
            Title: "Challenger Screen",
            Root: new Column([
                new Heading($"Counter: {state.Counter}"),
                new Button("btn-valid", "Valid Button", Emits(new ChallengerTick(1))),
                new Button("btn-no-emit", "No Emit Button", Intent: new Intent(IntentKind.Copy, "clipboard")),
                new Button("btn-alien-owner", "Alien Owner Button", new Emit("alien.screen", "challenger.tick")),
                new Field("fld-valid", "Valid Field", FieldKind.Text, state.Text, OnSubmit: Emits(new ChallengerSetText("default"))),
                new Field("fld-no-submit", "Field Without OnSubmit", FieldKind.Text, state.Text, OnSubmit: null),
                new Field("fld-alien-submit", "Field Alien Submit", FieldKind.Text, state.Text, OnSubmit: new Emit("alien.screen", "challenger.set-text"))
            ])
        );
    }

    [On]
    public Task When(ChallengerTick signal) => Show(State with { Counter = State.Counter + signal.Delta });

    [On]
    public Task When(ChallengerSetText signal) => Show(State with { Text = signal.Value });
}

public sealed class ScreenSignalsAndRefusalChallengerFacts
{
    private static async Task<(ChallengerScreen Screen, RecordingContext Context)> Attached()
    {
        var screen = new ChallengerScreen();
        var id = new NeuronId(new BrainId("alice"), "challenger.screen", "default");
        var context = new RecordingContext(id);
        NeuronAttachment.Attach(screen, context);
        await NeuronAttachment.Activate(screen, CancellationToken.None);
        return (screen, context);
    }

    // =========================================================================
    // Focus 1: ScreenSignals & ALC Leak Avoidance on Collectible Assemblies
    // =========================================================================

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static (WeakReference AlcRef, WeakReference TypeRef) ExerciseCollectibleScreenSignals()
    {
        var alc = new AssemblyLoadContext("CollectibleChallengerContext", isCollectible: true);
        using var stream = File.OpenRead(typeof(DummyScreen).Assembly.Location);
        var loadedAssembly = alc.LoadFromStream(stream);
        var loadedType = loadedAssembly.GetType(typeof(DummyScreen).FullName!)!;

        Assert.True(loadedType.IsCollectible, "Type loaded in collectible ALC must report IsCollectible=true.");

        // Exercise ScreenSignals.For and Find on collectible type
        var signals = ScreenSignals.For(loadedType);
        Assert.NotNull(signals);
        Assert.True(signals.ContainsKey(SignalAliases.Of(typeof(FirstSignal))));
        Assert.True(signals.ContainsKey("screen.second"));

        var found = ScreenSignals.Find(loadedType, "screen.second");
        Assert.NotNull(found);
        Assert.Equal(nameof(SecondSignal), found.Name);

        var unknown = ScreenSignals.Find(loadedType, "nonexistent.signal");
        Assert.Null(unknown);

        var alcRef = new WeakReference(alc);
        var typeRef = new WeakReference(loadedType);

        alc.Unload();
        return (alcRef, typeRef);
    }

    [Fact]
    public void ScreenSignals_CollectibleAssembly_BypassesCache_UnloadsCleanlyWithoutPinning()
    {
        var (alcRef, typeRef) = ExerciseCollectibleScreenSignals();

        for (var i = 0; i < 10 && (alcRef.IsAlive || typeRef.IsAlive); i++)
        {
            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();
        }

        Assert.False(alcRef.IsAlive, "FAILURE: Collectible AssemblyLoadContext was pinned in memory by ScreenSignals!");
        Assert.False(typeRef.IsAlive, "FAILURE: Collectible Type was pinned in memory by ScreenSignals!");
    }

    [Fact]
    public void ScreenSignals_NonCollectibleAssembly_CachesEfficiently_ReturnsSameReference()
    {
        Assert.False(typeof(ChallengerScreen).IsCollectible);

        var map1 = ScreenSignals.For(typeof(ChallengerScreen));
        var map2 = ScreenSignals.For(typeof(ChallengerScreen));

        Assert.NotNull(map1);
        Assert.Same(map1, map2);
        Assert.True(map1.ContainsKey("challenger.tick"));
        Assert.True(map1.ContainsKey("challenger.set-text"));
    }

    [Fact]
    public void ScreenSignals_NonCollectibleAssembly_ConcurrentAccess_IsThreadSafe()
    {
        var canonical = ScreenSignals.For(typeof(ChallengerScreen));

        Parallel.For(0, 50, _ =>
        {
            var map = ScreenSignals.For(typeof(ChallengerScreen));
            Assert.Same(canonical, map);

            var tickType = ScreenSignals.Find(typeof(ChallengerScreen), "challenger.tick");
            Assert.Equal(typeof(ChallengerTick), tickType);

            var textType = ScreenSignals.Find(typeof(ChallengerScreen), "challenger.set-text");
            Assert.Equal(typeof(ChallengerSetText), textType);
        });
    }

    [Fact]
    public void ScreenSignals_NullArguments_ThrowsArgumentNullException()
    {
        Assert.Throws<ArgumentNullException>(() => ScreenSignals.For(null!));
        Assert.Throws<ArgumentNullException>(() => ScreenSignals.Find(null!, "challenger.tick"));
        Assert.Throws<ArgumentNullException>(() => ScreenSignals.Find(typeof(ChallengerScreen), null!));
    }

    // =========================================================================
    // Focus 2: IScreen.Activate 4-Case Refusal Matrix & Zero Journal Side Effects
    // =========================================================================

    [Theory]
    [InlineData(2L)]   // Future generation (current is 1)
    [InlineData(0L)]   // Generation zero
    [InlineData(-1L)]  // Negative generation
    [InlineData(100L)] // Arbitrary future generation
    public async Task Activate_Case1_StaleGeneration_ThrowsActivationRefusedException_WithZeroJournaling(long staleGen)
    {
        var (screen, context) = await Attached();
        var initialView = await screen.Read();
        Assert.NotNull(initialView);
        Assert.Equal(1, initialView.Generation);

        var beforeWrites = context.Writes.Count;
        var beforeViewWrites = context.ViewWrites.Count;
        var beforePublished = context.Published.Count;
        var beforeState = screen.CurrentState;

        var ex = await Assert.ThrowsAsync<ActivationRefusedException>(() =>
            screen.Activate("challenger.screen", staleGen, "btn-valid"));

        Assert.Equal(ActivationRefusedException.Stale, ex.Reason);

        // Verification: Absolutely ZERO state or signal journaled
        Assert.Equal(beforeWrites, context.Writes.Count);
        Assert.Equal(beforeViewWrites, context.ViewWrites.Count);
        Assert.Equal(beforePublished, context.Published.Count);

        // State and view are untouched
        Assert.Equal(beforeState.Counter, screen.CurrentState.Counter);
        Assert.Equal(beforeState.Text, screen.CurrentState.Text);

        var viewAfter = await screen.Read();
        Assert.Equal(initialView.Generation, viewAfter?.Generation);
        Assert.Equal(initialView.View.GetRawText(), viewAfter?.View.GetRawText());
    }

    [Fact]
    public async Task Activate_UninitializedScreen_ThrowsActivationRefusedException_Stale()
    {
        var screen = new ChallengerScreen();
        // Do not activate - _view remains null
        var ex = await Assert.ThrowsAsync<ActivationRefusedException>(() =>
            screen.Activate("challenger.screen", 1, "btn-valid"));

        Assert.Equal(ActivationRefusedException.Stale, ex.Reason);
    }

    [Fact]
    public async Task Activate_Case1_MismatchedAlias_ThrowsActivationRefusedException_WithZeroJournaling()
    {
        var (screen, context) = await Attached();
        var initialView = await screen.Read();
        Assert.NotNull(initialView);

        var beforeWrites = context.Writes.Count;
        var beforeState = screen.CurrentState;

        var ex = await Assert.ThrowsAsync<ActivationRefusedException>(() =>
            screen.Activate("wrong.screen.alias", initialView.Generation, "btn-valid"));

        Assert.Equal(ActivationRefusedException.Stale, ex.Reason);
        Assert.Equal(beforeWrites, context.Writes.Count);
        Assert.Equal(beforeState.Counter, screen.CurrentState.Counter);
    }

    [Theory]
    [InlineData("btn-nonexistent")]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("random-key-not-in-tree")]
    public async Task Activate_Case2_UnknownNodeKey_ThrowsActivationRefusedException_WithZeroJournaling(string unknownKey)
    {
        var (screen, context) = await Attached();
        var initialView = await screen.Read();
        Assert.NotNull(initialView);

        var beforeWrites = context.Writes.Count;
        var beforeViewWrites = context.ViewWrites.Count;
        var beforePublished = context.Published.Count;
        var beforeState = screen.CurrentState;

        var ex = await Assert.ThrowsAsync<ActivationRefusedException>(() =>
            screen.Activate("challenger.screen", initialView.Generation, unknownKey));

        Assert.Equal(ActivationRefusedException.UnknownNode, ex.Reason);

        // Verification: Absolutely ZERO state or signal journaled
        Assert.Equal(beforeWrites, context.Writes.Count);
        Assert.Equal(beforeViewWrites, context.ViewWrites.Count);
        Assert.Equal(beforePublished, context.Published.Count);

        Assert.Equal(beforeState.Counter, screen.CurrentState.Counter);
        Assert.Equal(beforeState.Text, screen.CurrentState.Text);

        var viewAfter = await screen.Read();
        Assert.Equal(initialView.Generation, viewAfter?.Generation);
    }

    [Theory]
    [InlineData("btn-no-emit")]
    [InlineData("fld-no-submit")]
    public async Task Activate_Case3_NodeHasNoEmit_ThrowsActivationRefusedException_WithZeroJournaling(string noEmitKey)
    {
        var (screen, context) = await Attached();
        var initialView = await screen.Read();
        Assert.NotNull(initialView);

        var beforeWrites = context.Writes.Count;
        var beforeViewWrites = context.ViewWrites.Count;
        var beforePublished = context.Published.Count;
        var beforeState = screen.CurrentState;

        var ex = await Assert.ThrowsAsync<ActivationRefusedException>(() =>
            screen.Activate("challenger.screen", initialView.Generation, noEmitKey));

        Assert.Equal(ActivationRefusedException.NoEmit, ex.Reason);

        // Verification: Absolutely ZERO state or signal journaled
        Assert.Equal(beforeWrites, context.Writes.Count);
        Assert.Equal(beforeViewWrites, context.ViewWrites.Count);
        Assert.Equal(beforePublished, context.Published.Count);

        Assert.Equal(beforeState.Counter, screen.CurrentState.Counter);
        Assert.Equal(beforeState.Text, screen.CurrentState.Text);

        var viewAfter = await screen.Read();
        Assert.Equal(initialView.Generation, viewAfter?.Generation);
    }

    [Theory]
    [InlineData("btn-alien-owner")]
    [InlineData("fld-alien-submit")]
    public async Task Activate_Case4_ForeignOwner_ThrowsActivationRefusedException_WithZeroJournaling(string alienKey)
    {
        var (screen, context) = await Attached();
        var initialView = await screen.Read();
        Assert.NotNull(initialView);

        var beforeWrites = context.Writes.Count;
        var beforeViewWrites = context.ViewWrites.Count;
        var beforePublished = context.Published.Count;
        var beforeState = screen.CurrentState;

        var ex = await Assert.ThrowsAsync<ActivationRefusedException>(() =>
            screen.Activate("challenger.screen", initialView.Generation, alienKey));

        Assert.Equal(ActivationRefusedException.ForeignOwner, ex.Reason);

        // Verification: Absolutely ZERO state or signal journaled
        Assert.Equal(beforeWrites, context.Writes.Count);
        Assert.Equal(beforeViewWrites, context.ViewWrites.Count);
        Assert.Equal(beforePublished, context.Published.Count);

        Assert.Equal(beforeState.Counter, screen.CurrentState.Counter);
        Assert.Equal(beforeState.Text, screen.CurrentState.Text);

        var viewAfter = await screen.Read();
        Assert.Equal(initialView.Generation, viewAfter?.Generation);
    }

    [Fact]
    public async Task Activate_NullArguments_ThrowsArgumentNullException_WithZeroJournaling()
    {
        var (screen, context) = await Attached();
        var initialView = await screen.Read();
        Assert.NotNull(initialView);

        var beforeWrites = context.Writes.Count;

        await Assert.ThrowsAsync<ArgumentNullException>(() =>
            screen.Activate(null!, initialView.Generation, "btn-valid"));

        await Assert.ThrowsAsync<ArgumentNullException>(() =>
            screen.Activate("challenger.screen", initialView.Generation, null!));

        Assert.Equal(beforeWrites, context.Writes.Count);
    }

    [Fact]
    public async Task Activate_ValidInteraction_JournalsStateAndBumpsGeneration()
    {
        var (screen, context) = await Attached();
        var initialView = await screen.Read();
        Assert.NotNull(initialView);
        Assert.Equal(1, initialView.Generation);
        Assert.Equal(0, screen.CurrentState.Counter);

        var beforeWrites = context.Writes.Count;

        await screen.Activate("challenger.screen", initialView.Generation, "btn-valid");

        // Positive control: verify that a valid activation DOES journal and bump generation
        Assert.Equal(1, screen.CurrentState.Counter);
        Assert.Equal(beforeWrites + 1, context.Writes.Count);

        var updatedView = await screen.Read();
        Assert.NotNull(updatedView);
        Assert.Equal(2, updatedView.Generation);
        Assert.Contains("Counter: 1", updatedView.View.GetRawText());
    }
}
