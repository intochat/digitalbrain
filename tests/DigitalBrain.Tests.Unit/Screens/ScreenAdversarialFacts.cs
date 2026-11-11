using System.Text;
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

public sealed record AdversarialScreenState
{
    public string VisibleText { get; init; } = "Initial";
    public int HiddenCounter { get; init; }
    public string LastCallerBy { get; init; } = "";
    public string LastCallerValue { get; init; } = "";
}

[Signal("adversarial.update-visible")]
public sealed record UpdateVisible(string Text) : Signal;

[Signal("adversarial.update-hidden")]
public sealed record UpdateHidden(int Amount = 1) : Signal;

[Signal("adversarial.set-caller")]
public sealed record SetCallerWithBy(string Value, string? By = null) : Signal;

[Neuron("app.adversarial-test")]
public sealed class AdversarialScreen : Screen<AdversarialScreenState>
{
    protected override View Render(AdversarialScreenState state)
    {
        using var callerArgs = JsonDocument.Parse("""{"value": "btn-click"}""");
        return new View(
            Title: "Adversarial Screen",
            Root: new Column([
                new Heading($"Visible: {state.VisibleText}"),
                new Heading($"Caller: {state.LastCallerBy}"),
                new Button("btn-update-visible", "Update Visible", Emits(new UpdateVisible("Updated"))),
                new Button("btn-update-hidden", "Update Hidden", Emits(new UpdateHidden(1))),
                new Button("btn-set-caller", "Set Caller", new Emit(Alias, "adversarial.set-caller", callerArgs.RootElement.Clone())),
                new Button("btn-foreign", "Foreign", new Emit("other.screen", "adversarial.update-visible")),
                new Button("btn-no-emit", "No Emit", Intent: new Intent(IntentKind.Copy, "text")),
                new Field("fld-input", "input-label", FieldKind.Text, state.LastCallerValue, OnSubmit: new Emit(Alias, "adversarial.set-caller", callerArgs.RootElement.Clone()))
            ])
        );
    }

    [On]
    public Task When(UpdateVisible signal) => Show(State with { VisibleText = signal.Text });

    [On]
    public Task When(UpdateHidden signal) => Show(State with { HiddenCounter = State.HiddenCounter + signal.Amount });

    [On]
    public Task When(SetCallerWithBy signal) => Show(State with { LastCallerBy = signal.By ?? "", LastCallerValue = signal.Value });
}

public sealed record ExactPayloadState()
{
    public int TargetBytes { get; init; }
    public ExactPayloadState(int targetBytes) : this() => TargetBytes = targetBytes;
}

[Neuron("app.exact-payload-screen")]
public sealed class ExactPayloadScreen : Screen<ExactPayloadState>
{
    protected override View Render(ExactPayloadState state)
    {
        // Compute exact padding needed to hit target bytes
        var emptyView = new View(Title: "P", Root: new Heading(""));
        var baseJson = JsonSerializer.Serialize(emptyView, DigitalBrain.Invocation.RuntimeProtocol.OptionsFor(typeof(View)));
        var baseBytes = Encoding.UTF8.GetByteCount(baseJson);

        var needed = state.TargetBytes - baseBytes;
        var padding = needed > 0 ? new string('X', needed) : "";
        return new View(Title: "P", Root: new Heading(padding));
    }

    [On]
    public Task When(UpdateVisible signal) => Show(State);
}

public sealed class CasingByScreen : Screen<AdversarialScreenState>
{
    private readonly string _byPropertyName;

    public CasingByScreen() : this("By") { }
    public CasingByScreen(string byPropertyName) => _byPropertyName = byPropertyName;

    protected override View Render(AdversarialScreenState state)
    {
        using var jsonDoc = JsonDocument.Parse($$"""{"{{_byPropertyName}}": "attacker"}""");
        return new View(
            Title: "Casing By",
            Root: new Column([
                new Button("btn-casing", "Casing Attack", new Emit(Alias, "adversarial.set-caller", jsonDoc.RootElement.Clone()))
            ])
        );
    }

    [On]
    public Task When(SetCallerWithBy signal) => Show(State with { LastCallerBy = signal.By ?? "", LastCallerValue = signal.Value });
}

public sealed class OnSubmitByScreen : Screen<AdversarialScreenState>
{
    private readonly string _byPropertyName;

    public OnSubmitByScreen() : this("by") { }
    public OnSubmitByScreen(string byPropertyName) => _byPropertyName = byPropertyName;

    protected override View Render(AdversarialScreenState state)
    {
        using var jsonDoc = JsonDocument.Parse($$"""{"{{_byPropertyName}}": "attacker"}""");
        return new View(
            Title: "OnSubmit By",
            Root: new Column([
                new Field("fld-attacker", "label", FieldKind.Text, "", OnSubmit: new Emit(Alias, "adversarial.set-caller", jsonDoc.RootElement.Clone()))
            ])
        );
    }

    [On]
    public Task When(SetCallerWithBy signal) => Show(State with { LastCallerBy = signal.By ?? "", LastCallerValue = signal.Value });
}

public sealed class MultiCasingByScreen : Screen<AdversarialScreenState>
{
    protected override View Render(AdversarialScreenState state)
    {
        using var jsonDoc = JsonDocument.Parse("""{"by": "a1", "By": "a2", "BY": "a3", "bY": "a4"}""");
        return new View(
            Title: "Multi Casing",
            Root: new Column([
                new Button("btn-multi", "Multi Attack", new Emit(Alias, "adversarial.set-caller", jsonDoc.RootElement.Clone()))
            ])
        );
    }

    [On]
    public Task When(SetCallerWithBy signal) => Show(State);
}

public sealed class DeeplyNestedByScreen : Screen<AdversarialScreenState>
{
    private readonly string _casing;
    public DeeplyNestedByScreen(string casing = "bY") => _casing = casing;

    protected override View Render(AdversarialScreenState state)
    {
        using var jsonDoc = JsonDocument.Parse($$"""{"{{_casing}}": "deep-attacker"}""");
        return new View(
            Title: "Deeply Nested",
            Root: new Column([
                new Row([
                    new Column([
                        new Button("btn-deep", "Deep", new Emit(Alias, "adversarial.set-caller", jsonDoc.RootElement.Clone()))
                    ])
                ])
            ])
        );
    }

    [On]
    public Task When(SetCallerWithBy signal) => Show(State);
}

public sealed class ScreenAdversarialFacts
{
    private static async Task<(TScreen Screen, RecordingContext Context)> Attached<TScreen>(string alias = "app.adversarial-test") where TScreen : Neuron, new()
    {
        var screen = new TScreen();
        var id = new NeuronId(new BrainId("alice"), alias, "default");
        var context = new RecordingContext(id);
        NeuronAttachment.Attach(screen, context);
        await NeuronAttachment.Activate(screen, CancellationToken.None);
        return (screen, context);
    }

    // =========================================================================
    // 1. View Diff Invariants & ViewChanged Signal
    // =========================================================================

    [Fact]
    public async Task ViewDiff_IdenticalView_PreservesGeneration_EmitsNoViewChanged()
    {
        var (screen, context) = await Attached<AdversarialScreen>();
        var initialView = await screen.Read();
        Assert.NotNull(initialView);
        Assert.Equal(1, initialView.Generation);

        var initialWritesCount = context.Writes.Count;
        Assert.True(initialWritesCount > 0);

        // Update hidden counter: state changes, but rendered view JSON is 100% identical
        await screen.Request<UpdateHidden>(new UpdateHidden(5));

        var currentView = await screen.Read();
        Assert.NotNull(currentView);
        Assert.Equal(1, currentView.Generation); // Must remain generation 1

        // Check the save entry: must contain StateChanged, but MUST NOT contain ViewChanged
        var lastWrite = context.Writes.Last();
        var viewChangedSignals = lastWrite.Signals.OfType<ViewChanged>().ToList();
        Assert.Empty(viewChangedSignals);
    }

    [Fact]
    public async Task ViewDiff_MinimalChange_SingleCharacterDifference_IncrementsGeneration_EmitsViewChanged()
    {
        var (screen, context) = await Attached<AdversarialScreen>();
        var view1 = await screen.Read();
        Assert.NotNull(view1);
        Assert.Equal(1, view1.Generation);

        // Change VisibleText by a single character
        await screen.Request<UpdateVisible>(new UpdateVisible("A"));

        var view2 = await screen.Read();
        Assert.NotNull(view2);
        Assert.Equal(2, view2.Generation);
        Assert.Contains("Visible: A", view2.View.GetRawText());

        var lastWrite = context.Writes.Last();
        var viewChanged = Assert.Single(lastWrite.Signals.OfType<ViewChanged>());
        Assert.Equal("app.adversarial-test", viewChanged.Alias);
        Assert.Equal(2, viewChanged.Generation);
    }

    [Fact]
    public async Task ViewDiff_OscillatingStates_IncrementsGenerationOnlyWhenWireDiffers()
    {
        var (screen, context) = await Attached<AdversarialScreen>();

        // Turn 1: Initial -> Gen 1 (emits ViewChanged(1))
        var v1 = await screen.Read();
        Assert.NotNull(v1);
        Assert.Equal(1, v1.Generation);

        // Turn 2: Switch to "B" -> Gen 2 (emits ViewChanged(2))
        await screen.Request<UpdateVisible>(new UpdateVisible("B"));
        var v2 = await screen.Read();
        Assert.NotNull(v2);
        Assert.Equal(2, v2.Generation);
        Assert.Single(context.Writes.Last().Signals.OfType<ViewChanged>());

        // Turn 3: Switch back to "Initial" -> Wire differs from "B", so Gen 3 (emits ViewChanged(3))
        await screen.Request<UpdateVisible>(new UpdateVisible("Initial"));
        var v3 = await screen.Read();
        Assert.NotNull(v3);
        Assert.Equal(3, v3.Generation);
        Assert.Single(context.Writes.Last().Signals.OfType<ViewChanged>());

        // Turn 4: Set to "Initial" again -> Wire matches current view, so Gen 3 remains!
        await screen.Request<UpdateVisible>(new UpdateVisible("Initial"));
        var v4 = await screen.Read();
        Assert.NotNull(v4);
        Assert.Equal(3, v4.Generation);
        // NO ViewChanged emitted on Turn 4
        Assert.Empty(context.Writes.Last().Signals.OfType<ViewChanged>());
    }

    // =========================================================================
    // 2. Security & "by" Forgery Invariants
    // =========================================================================

    [Theory]
    [InlineData("by")]
    [InlineData("By")]
    [InlineData("BY")]
    [InlineData("bY")]
    public async Task Security_AllCasingPermutations_InButtonEmit_ThrowsInvalidOperationException(string casing)
    {
        var invalid = new CasingByScreen(casing);
        var context = new RecordingContext(new NeuronId(new BrainId("alice"), "CasingByScreen", "default"));
        NeuronAttachment.Attach(invalid, context);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => NeuronAttachment.Activate(invalid, CancellationToken.None));
        Assert.Equal("The view of 'CasingByScreen' declares the host-reserved 'by' in an emit's args.", ex.Message);
    }

    [Theory]
    [InlineData("by")]
    [InlineData("By")]
    [InlineData("BY")]
    [InlineData("bY")]
    public async Task Security_AllCasingPermutations_InOnSubmitEmit_ThrowsInvalidOperationException(string casing)
    {
        var invalid = new OnSubmitByScreen(casing);
        var context = new RecordingContext(new NeuronId(new BrainId("alice"), "OnSubmitByScreen", "default"));
        NeuronAttachment.Attach(invalid, context);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => NeuronAttachment.Activate(invalid, CancellationToken.None));
        Assert.Equal("The view of 'OnSubmitByScreen' declares the host-reserved 'by' in an emit's args.", ex.Message);
    }

    [Fact]
    public async Task Security_MultipleSimultaneousCasingVariations_InEmit_ThrowsInvalidOperationException()
    {
        var invalid = new MultiCasingByScreen();
        var context = new RecordingContext(new NeuronId(new BrainId("alice"), "MultiCasingByScreen", "default"));
        NeuronAttachment.Attach(invalid, context);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => NeuronAttachment.Activate(invalid, CancellationToken.None));
        Assert.Equal("The view of 'MultiCasingByScreen' declares the host-reserved 'by' in an emit's args.", ex.Message);
    }

    [Theory]
    [InlineData("by")]
    [InlineData("By")]
    [InlineData("BY")]
    [InlineData("bY")]
    public async Task Security_DeeplyNestedView_CasingVariationsInArgs_ThrowsInvalidOperationException(string casing)
    {
        var invalid = new DeeplyNestedByScreen(casing);
        var context = new RecordingContext(new NeuronId(new BrainId("alice"), "DeeplyNestedByScreen", "default"));
        NeuronAttachment.Attach(invalid, context);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => NeuronAttachment.Activate(invalid, CancellationToken.None));
        Assert.Equal("The view of 'DeeplyNestedByScreen' declares the host-reserved 'by' in an emit's args.", ex.Message);
    }

    [Theory]
    [InlineData("by")]
    [InlineData("By")]
    [InlineData("BY")]
    [InlineData("bY")]
    public async Task Security_AllCasingPermutations_InRequestArgs_StrippedCompletely(string casing)
    {
        var (screen, _) = await Attached<AdversarialScreen>();

        using var forgedArgs = JsonDocument.Parse($$"""{"value": "payload-{{casing}}", "{{casing}}": "forged-{{casing}}"}""");
        await screen.Request("adversarial.set-caller", forgedArgs.RootElement.Clone());

        var view = await screen.Read();
        Assert.NotNull(view);
        var rawText = view.View.GetRawText();
        Assert.False(rawText.Contains($"forged-{casing}"), $"VULNERABILITY: Request with '{casing}' in args forged the caller identity!");
        Assert.Contains("Caller: ", rawText);
        Assert.DoesNotContain($"Caller: forged-{casing}", rawText);
    }

    [Fact]
    public async Task Security_Request_MultipleSimultaneousCasingVariations_AllStripped()
    {
        var (screen, _) = await Attached<AdversarialScreen>();

        using var forgedArgs = JsonDocument.Parse("""{"value": "multi-payload", "by": "f1", "By": "f2", "BY": "f3", "bY": "f4"}""");
        await screen.Request("adversarial.set-caller", forgedArgs.RootElement.Clone());

        var view = await screen.Read();
        Assert.NotNull(view);
        var rawText = view.View.GetRawText();
        Assert.DoesNotContain("f1", rawText);
        Assert.DoesNotContain("f2", rawText);
        Assert.DoesNotContain("f3", rawText);
        Assert.DoesNotContain("f4", rawText);
    }

    [Fact]
    public async Task Security_Request_NonStringCasingVariations_HandledSafelyAndStripped()
    {
        var (screen, _) = await Attached<AdversarialScreen>();

        // Attacker sends non-string or nested objects under casing variations of "by"
        using var forgedArgs = JsonDocument.Parse("""{"value": "non-string", "BY": 12345, "bY": ["arr1"], "By": {"inner": true}}""");
        await screen.Request("adversarial.set-caller", forgedArgs.RootElement.Clone());

        var view = await screen.Read();
        Assert.NotNull(view);
        var rawText = view.View.GetRawText();
        Assert.DoesNotContain("12345", rawText);
        Assert.DoesNotContain("arr1", rawText);
        Assert.DoesNotContain("inner", rawText);
    }

    [Fact]
    public async Task Security_Activate_HostAssertedBy_InjectedAndOverridesClient()
    {
        var (screen, _) = await Attached<AdversarialScreen>();
        var view = await screen.Read();
        Assert.NotNull(view);

        await screen.Activate("app.adversarial-test", view.Generation, "fld-input", fieldValue: "typed-text", by: "authenticated-user@intochat.com");

        var updatedView = await screen.Read();
        Assert.NotNull(updatedView);
        Assert.Contains("authenticated-user@intochat.com", updatedView.View.GetRawText());
    }

    [Theory]
    [InlineData("by")]
    [InlineData("By")]
    [InlineData("BY")]
    [InlineData("bY")]
    public async Task Security_PreseededLegacyView_AllCasingPermutations_HostByWins(string casing)
    {
        var screen = new AdversarialScreen();
        var id = new NeuronId(new BrainId("alice"), "app.adversarial-test", "default");
        var context = new RecordingContext(id);
        using var jsonDoc = JsonDocument.Parse($$"""{"{{casing}}": "attacker-{{casing}}", "value": "btn-click"}""");
        var preloadedView = new View(
            Title: "Adversarial Screen",
            Root: new Column([
                new Button("btn-set-caller", "Set Caller", new Emit("app.adversarial-test", "adversarial.set-caller", jsonDoc.RootElement.Clone()))
            ])
        );
        var wire = JsonSerializer.SerializeToElement(preloadedView, DigitalBrain.Invocation.RuntimeProtocol.OptionsFor(typeof(View)));
        context.Store(new StoredState<AdversarialScreenState>(new AdversarialScreenState(), new PublishedView("app.adversarial-test", 1, wire)));
        NeuronAttachment.Attach(screen, context);
        await NeuronAttachment.Activate(screen, CancellationToken.None);

        var view = await screen.Read();
        Assert.NotNull(view);

        await screen.Activate("app.adversarial-test", view.Generation, "btn-set-caller", by: "real-alice@intochat.com");

        var updatedView = await screen.Read();
        Assert.NotNull(updatedView);
        var raw = updatedView.View.GetRawText();
        Assert.DoesNotContain($"attacker-{casing}", raw);
        Assert.Contains("real-alice@intochat.com", raw);
    }

    [Theory]
    [InlineData("by")]
    [InlineData("By")]
    [InlineData("BY")]
    [InlineData("bY")]
    public async Task Security_PreseededLegacyView_AllCasingPermutations_NullHostBy_StripsAttacker(string casing)
    {
        var screen = new AdversarialScreen();
        var id = new NeuronId(new BrainId("alice"), "app.adversarial-test", "default");
        var context = new RecordingContext(id);
        using var jsonDoc = JsonDocument.Parse($$"""{"{{casing}}": "attacker-{{casing}}", "value": "btn-click"}""");
        var preloadedView = new View(
            Title: "Adversarial Screen",
            Root: new Column([
                new Button("btn-set-caller", "Set Caller", new Emit("app.adversarial-test", "adversarial.set-caller", jsonDoc.RootElement.Clone()))
            ])
        );
        var wire = JsonSerializer.SerializeToElement(preloadedView, DigitalBrain.Invocation.RuntimeProtocol.OptionsFor(typeof(View)));
        context.Store(new StoredState<AdversarialScreenState>(new AdversarialScreenState(), new PublishedView("app.adversarial-test", 1, wire)));
        NeuronAttachment.Attach(screen, context);
        await NeuronAttachment.Activate(screen, CancellationToken.None);

        var view = await screen.Read();
        Assert.NotNull(view);

        // Host provides NO authenticated identity (null)
        await screen.Activate("app.adversarial-test", view.Generation, "btn-set-caller", by: null);

        var updatedView = await screen.Read();
        Assert.NotNull(updatedView);
        var raw = updatedView.View.GetRawText();
        Assert.DoesNotContain($"attacker-{casing}", raw);
    }

    // =========================================================================
    // 3. Payload Cap Boundary Conditions (2MB)
    // =========================================================================

    [Fact]
    public async Task PayloadCap_ExactBoundary_2097152Bytes_Succeeds()
    {
        var screen = new ExactPayloadScreen();
        var context = new RecordingContext(new NeuronId(new BrainId("alice"), "ExactPayloadScreen", "default"));
        NeuronAttachment.Attach(screen, context);

        // Pre-set target to exact limit: UnitLimits.MaxStateBytes = 2,097,152
        context.Store(new StoredState<ExactPayloadState>(new ExactPayloadState(UnitLimits.MaxStateBytes), null));

        // Must succeed without throwing
        await NeuronAttachment.Activate(screen, CancellationToken.None);

        var view = await screen.Read();
        Assert.NotNull(view);
        var bytes = Encoding.UTF8.GetByteCount(view.View.GetRawText());
        Assert.Equal(UnitLimits.MaxStateBytes, bytes);
    }

    [Fact]
    public async Task PayloadCap_ExactBoundary_2097153Bytes_ThrowsInvalidOperationException()
    {
        var screen = new ExactPayloadScreen();
        var context = new RecordingContext(new NeuronId(new BrainId("alice"), "ExactPayloadScreen", "default"));
        NeuronAttachment.Attach(screen, context);

        // Pre-set target to 1 byte over limit: 2,097,153 bytes
        context.Store(new StoredState<ExactPayloadState>(new ExactPayloadState(UnitLimits.MaxStateBytes + 1), null));

        // Must throw InvalidOperationException
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => NeuronAttachment.Activate(screen, CancellationToken.None));
        Assert.Contains($"exceeds the {UnitLimits.MaxStateBytes}-byte cap", ex.Message);
    }

    // =========================================================================
    // 4. Activation Invariants & Refusal Reasons
    // =========================================================================

    [Fact]
    public async Task Activate_StaleGeneration_RefusedWithStale()
    {
        var (screen, _) = await Attached<AdversarialScreen>();
        var view = await screen.Read();
        Assert.NotNull(view);

        var ex = await Assert.ThrowsAsync<ActivationRefusedException>(() =>
            screen.Activate("app.adversarial-test", view.Generation + 5, "btn-update-visible"));
        Assert.Equal(ActivationRefusedException.Stale, ex.Reason);
    }

    [Fact]
    public async Task Activate_UnknownNode_RefusedWithUnknownNode()
    {
        var (screen, _) = await Attached<AdversarialScreen>();
        var view = await screen.Read();
        Assert.NotNull(view);

        var ex = await Assert.ThrowsAsync<ActivationRefusedException>(() =>
            screen.Activate("app.adversarial-test", view.Generation, "non-existent-button"));
        Assert.Equal(ActivationRefusedException.UnknownNode, ex.Reason);
    }

    [Fact]
    public async Task Activate_NoEmitNode_RefusedWithNoEmit()
    {
        var (screen, _) = await Attached<AdversarialScreen>();
        var view = await screen.Read();
        Assert.NotNull(view);

        var ex = await Assert.ThrowsAsync<ActivationRefusedException>(() =>
            screen.Activate("app.adversarial-test", view.Generation, "btn-no-emit"));
        Assert.Equal(ActivationRefusedException.NoEmit, ex.Reason);
    }

    [Fact]
    public async Task Activate_ForeignOwner_RefusedWithForeignOwner()
    {
        var (screen, _) = await Attached<AdversarialScreen>();
        var view = await screen.Read();
        Assert.NotNull(view);

        var ex = await Assert.ThrowsAsync<ActivationRefusedException>(() =>
            screen.Activate("app.adversarial-test", view.Generation, "btn-foreign"));
        Assert.Equal(ActivationRefusedException.ForeignOwner, ex.Reason);
    }
}
