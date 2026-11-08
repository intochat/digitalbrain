using DigitalBrain.Spi;
using DigitalBrain.UI;
using OS = DigitalBrain.Testing.TestOS;

namespace DigitalBrain.Testing.Tests.Unit.Synapses;

// A unit whose view counts polls and offers a refresh button it owns, and one owned by another alias.
[State("vw")]
public sealed record ViewBoard(int Polls, int Refreshes, string? Value)
{
    public ViewBoard() : this(0, 0, null) { }
}

public static class ViewBoardUnit
{
    [Synapse("x.polled", "vw")]
    public static ViewBoard Seen(SignalEnvelope s, ViewBoard b) => b with { Polls = b.Polls + 1 };

    [Synapse("x.refresh", "vw")]
    public static ViewBoard Refreshed(SignalEnvelope s, ViewBoard b)
        => b with { Refreshes = b.Refreshes + 1, Value = s.Payload.TryGetProperty("value", out var v) ? v.GetString() : null };

    [Synapse("vw")]
    public static View Show(ViewBoard b) => new("Board", new Row(
    [
        new Badge(b.Polls.ToString(System.Globalization.CultureInfo.InvariantCulture)),
        new Button("Refresh", new ButtonAction("vw", "x.refresh", [])) { Key = "refresh", Emit = new Emit("vw", "x.refresh") },
        new Button("Foreign", new ButtonAction("other.alias", "x.refresh", [])) { Key = "foreign", Emit = new Emit("other.alias", "x.refresh") },
    ]));
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

        await views.Activate("vw", before.Generation, "refresh", "now");
        await os.Idle();

        var state = await ((IBrainObserver)os).State(Board, Ct);
        Assert.Equal(1, state?.GetProperty("refreshes").GetInt32());
        Assert.Equal("now", state?.GetProperty("value").GetString());
        Assert.Contains(os.Journal(Board.Brain).Signals(Board), item => SignalAliases.Of(item.Signal) == "x.refresh");
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

        var refused = await Assert.ThrowsAsync<ActivationRefusedException>(() => views.Activate("vw", view.Generation, "foreign", null));
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

        var refused = await Assert.ThrowsAsync<ActivationRefusedException>(() => views.Activate("vw", view.Generation - 1, "refresh", null));

        Assert.Equal(ActivationRefusedException.Stale, refused.Reason);
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
}
