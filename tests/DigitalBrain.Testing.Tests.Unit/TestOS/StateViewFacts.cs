using DigitalBrain.Spi;
using DigitalBrain.UI;
using OS = DigitalBrain.Testing.TestOS;

namespace DigitalBrain.Testing.Tests.Unit.Synapses;

// A unit whose view counts polls and offers a refresh button it owns, one owned by another alias, one whose reaction fails, and
// one that emits nothing; the refresh button's args carry a "key" that names no node.
[State("vw")]
public sealed record ViewBoard(int Polls, int Refreshes, string? Value, string? TraceId, string? CausationId, string? By = null)
{
    public ViewBoard() : this(0, 0, null, null, null) { }
}

public static class ViewBoardUnit
{
    [Synapse("x.polled", "vw")]
    public static ViewBoard Seen(SignalEnvelope s, ViewBoard b) => b with { Polls = b.Polls + 1 };

    [Synapse("x.refresh", "vw")]
    public static ViewBoard Refreshed(SignalEnvelope s, ViewBoard b)
        => b with
        {
            Refreshes = b.Refreshes + 1,
            Value = s.Payload.TryGetProperty("value", out var v) ? v.GetString() : null,
            TraceId = s.TraceId,
            CausationId = s.CausationId,
            By = s.Payload.TryGetProperty("by", out var by) ? by.GetString() : null
        };

    [Synapse("x.boom", "vw")]
    public static ViewBoard Boom(SignalEnvelope s, ViewBoard b) => throw new InvalidOperationException("boom");

    [Synapse("unit.answered", "vw")]
    public static ViewBoard Forged(SignalEnvelope s, ViewBoard b) => b with { Refreshes = b.Refreshes + 100 };

    [Synapse("vw")]
    public static View Show(ViewBoard b) => new("Board", new Row(
    [
        new Badge(b.Polls.ToString(System.Globalization.CultureInfo.InvariantCulture)),
        new Button("refresh", "Refresh", new Emit("vw", "x.refresh", System.Text.Json.JsonSerializer.SerializeToElement(new { key = "hidden", kind = "button" }))),
        new Button("foreign", "Foreign", new Emit("other.alias", "x.refresh")),
        new Button("boom", "Boom", new Emit("vw", "x.boom")),
        new Button("plain", "Plain", Intent: new Intent(IntentKind.Copy, "x")),
        new Button("forge", "Forge", new Emit("vw", "unit.answered")),
    ]));
}

// A unit whose view declares the host-reserved "by" in an emit's args: the view is refused at publish.
[State("vwb")]
public sealed record SpoofBoard(int Polls)
{
    public SpoofBoard() : this(0) { }
}

public static class SpoofBoardUnit
{
    [Synapse("x.polled", "vwb")]
    public static SpoofBoard Seen(SignalEnvelope s, SpoofBoard b) => b with { Polls = b.Polls + 1 };

    [Synapse("vwb")]
    public static View Show(SpoofBoard b) => new("Spoof", new Button("send", "Send", new Emit("vwb", "x.send", System.Text.Json.JsonSerializer.SerializeToElement(new { by = "admin" }))));
}

public sealed class StateViewFacts
{
    private static readonly NeuronId Installs = NeuronId.Parse("sv/synapse.installs/default");
    private static readonly NeuronId Board = NeuronId.Parse("sv/vw/default");
    private static readonly NeuronId Pollster = NeuronId.Parse("sv/poller/a");

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static async Task<OS> Started()
    {
        var os = OS.Local().Register(typeof(StateViewFacts).Assembly);
        os.Confirmations.ApproveAll();
        var outcome = await os.Get<IInstalls>(Installs).Install(new InstallRequest("vw", new Dictionary<string, string>()), Ct);
        Assert.Equal(InstallStatus.Pending, outcome.Status);
        await os.Idle();
        await os.Get<IPoller>(Pollster).Poll(1);
        await os.Idle();
        return os;
    }

    [Fact]
    public async Task ReadReturnsNullBeforeTheFirstSave()
    {
        await using var os = OS.Local().Register(typeof(StateViewFacts).Assembly);
        os.Confirmations.ApproveAll();
        await os.Get<IInstalls>(Installs).Install(new InstallRequest("vw", new Dictionary<string, string>()), Ct);
        await os.Idle();
        var reader = os.Get<IStateReader>(Board);

        Assert.Equal(System.Text.Json.JsonValueKind.Null, (await reader.Read("vw")).ValueKind);
        await os.Get<IPoller>(Pollster).Poll(1);
        await os.Idle();
        Assert.Equal(1, (await reader.Read("vw")).GetProperty("polls").GetInt32());
    }

    [Fact]
    public async Task AViewSynapseIsReadable()
    {
        await using var os = await Started();

        var published = await os.Get<IViewReader>(Board).Read("vw");

        Assert.NotNull(published);
        Assert.Equal("vw", published.Alias);
        var root = published.View.GetProperty("root");
        Assert.Equal("row", root.GetProperty("kind").GetString());
        Assert.Equal("1", root.GetProperty("children")[0].GetProperty("label").GetString());
        Assert.Contains(os.Journal(Board.Brain).Signals(Board), item => item.Signal is ViewChanged { Alias: "vw" } changed && changed.Generation == published.Generation);
        Assert.Empty(os.Failures);
    }

    [Fact]
    public async Task ActivatePublishesTheEmitToTheOwner()
    {
        await using var os = await Started();
        var views = os.Get<IViewReader>(Board);
        var before = (await views.Read("vw"))!;

        await views.Activate("vw", before.Generation, "refresh", "now", null);
        await os.Idle();

        var state = await ((IBrainObserver)os).State(Board, Ct);
        Assert.Equal(1, state?.GetProperty("refreshes").GetInt32());
        Assert.Equal("now", state?.GetProperty("value").GetString());
        var emit = Assert.Single(os.Journal(Board.Brain).Signals(Board), item => SignalAliases.Of(item.Signal) == "x.refresh");
        Assert.Null(state?.GetProperty("causationId").GetString());
        Assert.NotNull(emit.TraceId);
        Assert.Equal(emit.TraceId, state?.GetProperty("traceId").GetString());
        Assert.True((await views.Read("vw"))!.Generation > before.Generation);
        Assert.Empty(os.Failures);
    }

    [Fact]
    public async Task ActivateRefusesAForeignOwner()
    {
        await using var os = await Started();
        var views = os.Get<IViewReader>(Board);
        var view = (await views.Read("vw"))!;
        var journaled = os.Journal(Board.Brain).Signals(Board).Count;

        var refused = await Assert.ThrowsAsync<ActivationRefusedException>(() => views.Activate("vw", view.Generation, "foreign", null, null));
        await os.Idle();

        Assert.Equal(ActivationRefusedException.ForeignOwner, refused.Reason);
        Assert.Equal(journaled, os.Journal(Board.Brain).Signals(Board).Count);
    }

    [Fact]
    public async Task ActivateRefusesAStaleGeneration()
    {
        await using var os = await Started();
        var views = os.Get<IViewReader>(Board);
        var view = (await views.Read("vw"))!;

        var journaled = os.Journal(Board.Brain).Signals(Board).Count;

        var refused = await Assert.ThrowsAsync<ActivationRefusedException>(() => views.Activate("vw", view.Generation - 1, "refresh", null, null));
        await os.Idle();

        Assert.Equal(ActivationRefusedException.Stale, refused.Reason);
        Assert.Equal(journaled, os.Journal(Board.Brain).Signals(Board).Count);
    }

    [Fact]
    public async Task ActivateRefusesAnUnknownNode()
    {
        await using var os = await Started();
        var views = os.Get<IViewReader>(Board);
        var view = (await views.Read("vw"))!;
        var journaled = os.Journal(Board.Brain).Signals(Board).Count;

        var refused = await Assert.ThrowsAsync<ActivationRefusedException>(() => views.Activate("vw", view.Generation, "missing", null, null));

        Assert.Equal(ActivationRefusedException.UnknownNode, refused.Reason);
        Assert.Equal(journaled, os.Journal(Board.Brain).Signals(Board).Count);
    }

    [Fact]
    public async Task ActivateRefusesANodeWithoutEmit()
    {
        await using var os = await Started();
        var views = os.Get<IViewReader>(Board);
        var view = (await views.Read("vw"))!;
        var journaled = os.Journal(Board.Brain).Signals(Board).Count;

        var refused = await Assert.ThrowsAsync<ActivationRefusedException>(() => views.Activate("vw", view.Generation, "plain", null, null));

        Assert.Equal(ActivationRefusedException.NoEmit, refused.Reason);
        Assert.Equal(journaled, os.Journal(Board.Brain).Signals(Board).Count);
    }

    [Fact]
    public async Task ActivateStampsTheHostAssertedBy()
    {
        await using var os = await Started();
        var views = os.Get<IViewReader>(Board);
        var view = (await views.Read("vw"))!;

        await views.Activate("vw", view.Generation, "refresh", null, "p1");
        await os.Idle();

        var emit = Assert.Single(os.Journal(Board.Brain).Signals(Board), item => SignalAliases.Of(item.Signal) == "x.refresh");
        Assert.Equal("p1", (await ((IBrainObserver)os).State(Board, Ct))?.GetProperty("by").GetString());
        Assert.Empty(os.Failures);
    }

    [Fact]
    public async Task ActivateWithoutByEmitsNoBy()
    {
        await using var os = await Started();
        var views = os.Get<IViewReader>(Board);
        var view = (await views.Read("vw"))!;

        await views.Activate("vw", view.Generation, "refresh", "now", null);
        await os.Idle();

        Assert.Equal(System.Text.Json.JsonValueKind.Null, (await ((IBrainObserver)os).State(Board, Ct))?.GetProperty("by").ValueKind);
    }

    [Fact]
    public void AnAuthorDeclaredByNeverSurvives()
    {
        var args = System.Text.Json.JsonElement.Parse("""{"by":"admin","x":1}""");

        Assert.False(ViewNodes.Payload(args, null, null).TryGetProperty("by", out _));
        Assert.False(ViewNodes.Payload(args, "v", null).TryGetProperty("by", out _));
        Assert.Equal("p1", ViewNodes.Payload(args, null, "p1").GetProperty("by").GetString());
        Assert.Equal(1, ViewNodes.Payload(args, null, "p1").GetProperty("x").GetInt32());
    }

    [Fact]
    public async Task AViewDeclaringByIsRefusedAtPublish()
    {
        await using var os = OS.Local().Register(typeof(StateViewFacts).Assembly);
        os.Confirmations.ApproveAll();
        await os.Get<IInstalls>(Installs).Install(new InstallRequest("vwb", new Dictionary<string, string>()), Ct);
        await os.Idle();
        await os.Get<IPoller>(Pollster).Poll(1);
        await os.Idle();
        var spoof = NeuronId.Parse("sv/vwb/default");

        Assert.Equal(1, (await os.Get<IStateReader>(spoof).Read("vwb")).GetProperty("polls").GetInt32());
        Assert.Null(await os.Get<IViewReader>(spoof).Read("vwb"));
        Assert.DoesNotContain(os.Journal(spoof.Brain).Signals(spoof), item => item.Signal is ViewChanged);
        Assert.True(ViewNodes.DeclaresBy(System.Text.Json.JsonElement.Parse("""{"root":{"kind":"button","emit":{"owner":"a","signal":"s","args":{"by":"x"}}}}""")));
        Assert.False(ViewNodes.DeclaresBy(System.Text.Json.JsonElement.Parse("""{"root":{"kind":"button","emit":{"owner":"a","signal":"s","args":{"who":"x"}}}}""")));
    }

    [Fact]
    public async Task FindIgnoresKeysInsideEmitArgs()
    {
        await using var os = await Started();
        var views = os.Get<IViewReader>(Board);
        var view = (await views.Read("vw"))!;
        Assert.Contains("\"hidden\"", view.View.GetRawText(), StringComparison.Ordinal);

        var refused = await Assert.ThrowsAsync<ActivationRefusedException>(() => views.Activate("vw", view.Generation, "hidden", null, null));

        Assert.Equal(ActivationRefusedException.UnknownNode, refused.Reason);
        Assert.Null(ViewNodes.Find(System.Text.Json.JsonElement.Parse("""{"kind":"row","children":[{"kind":"button","emit":{"owner":"a","signal":"s","args":{"kind":"x","key":"k"}}}]}"""), "k"));
    }

    [Fact]
    public async Task AFailingReactionLeavesTheEmitJournaled()
    {
        await using var os = await Started();
        var finished = new System.Collections.Concurrent.ConcurrentQueue<SynapseFinished>();
        await using var listener = await os.ClusterListeners.Listen<SynapseFinished>("finished", (_, signal, _) =>
        {
            finished.Enqueue(signal);
            return Task.CompletedTask;
        });
        var views = os.Get<IViewReader>(Board);
        var view = (await views.Read("vw"))!;
        var before = await ((IBrainObserver)os).State(Board, Ct);

        await Assert.ThrowsAsync<InvalidOperationException>(() => views.Activate("vw", view.Generation, "boom", null, null));
        await os.Idle();

        Assert.Single(os.Journal(Board.Brain).Signals(Board), item => SignalAliases.Of(item.Signal) == "x.boom");
        Assert.Single(os.Journal(Board.Brain).Signals(Board), item => SignalAliases.Of(item.Signal) == "synapse.failed");
        Assert.Equal(before?.GetRawText(), (await ((IBrainObserver)os).State(Board, Ct))?.GetRawText());
        Assert.Contains(finished, f => f.Function.EndsWith(".Boom", StringComparison.Ordinal) && f.Outcome == "failed");
    }

    [Fact]
    public async Task TheViewSurvivesDeactivationAndRestart()
    {
        await using var os = await Started();
        var before = (await os.Get<IViewReader>(Board).Read("vw"))!;

        await os.Deactivate(Board);
        await os.Idle();
        var deactivated = (await os.Get<IViewReader>(Board).Read("vw"))!;
        await os.Restart();
        var restarted = (await os.Get<IViewReader>(Board).Read("vw"))!;

        Assert.Equal(before.Generation, deactivated.Generation);
        Assert.Equal(before.Generation, restarted.Generation);
        Assert.Equal(before.View.GetRawText(), restarted.View.GetRawText());
        await os.Get<IPoller>(Pollster).Poll(2);
        await os.Idle();
        var next = (await os.Get<IViewReader>(Board).Read("vw"))!;
        Assert.Equal(before.Generation + 1, next.Generation);
        Assert.Equal("2", next.View.GetProperty("root").GetProperty("children")[0].GetProperty("label").GetString());
        Assert.Empty(os.Failures);
    }

    [Fact]
    public async Task AViewButtonCannotForgeAnAnswer()
    {
        await using var os = await Started();
        var views = os.Get<IViewReader>(Board);
        var view = (await views.Read("vw"))!;

        await views.Activate("vw", view.Generation, "forge", null, null);
        await os.Idle();

        Assert.Equal(0, (await ((IBrainObserver)os).State(Board, Ct))?.GetProperty("refreshes").GetInt32());
        Assert.Empty(os.Failures);
    }
}
