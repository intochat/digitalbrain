using DigitalBrain.Compositions;
using DigitalBrain.Flutter;
using DigitalBrain.Testing;
using DigitalBrain.Time;
using Xunit;

namespace DigitalBrain.OS.Bdd.Tests;

public sealed class ShellAndSurfaceCompositions
{
    private const string ShellName = "desk";
    private const string CountdownName = "timer";
    private const string ModelName = "assistant";
    private const string PaneReply = "hello from pane";
    private const string PanePrompt = "ping";
    private const string SecondSceneKey = "settings";
    private const string SecondSceneTitle = "Settings";

    private static readonly TimeSpan CountdownDuration = TimeSpan.FromMinutes(5);

    [Fact(DisplayName = "OpenHome is shell-only — journals SceneOpened for the home scene only")]
    public async Task OpenHomeCompositionJournalsSceneOpened()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var test = await OSCluster.Fixture.CreateBrainAsync(cancellationToken);
        var shell = test.Neuron<IShell>(ShellName);

        await new OpenHome().RunAsync(test.Client, ShellName, cancellationToken);

        var opened = await shell.Outgoing.NextAsync<SceneOpened>(cancellationToken);
        Assert.Equal(OpenHome.SceneKey, opened.Synapse.SceneKey);
        Assert.Equal(OpenHome.SceneTitle, opened.Synapse.Title);
    }

    [Fact(DisplayName = "NavigateShell is shell-only — journals multiple SceneOpened facts in order")]
    public async Task NavigateShellJournalsMultipleScenes()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var test = await OSCluster.Fixture.CreateBrainAsync(cancellationToken);
        var shell = test.Neuron<IShell>(ShellName);

        await new NavigateShell().RunAsync(
            test.Client,
            ShellName,
            [
                (OpenHome.SceneKey, OpenHome.SceneTitle),
                (SecondSceneKey, SecondSceneTitle),
            ],
            cancellationToken);

        var first = await shell.Outgoing.NextAsync<SceneOpened>(cancellationToken);
        var second = await shell.Outgoing.NextAsync<SceneOpened>(cancellationToken);
        Assert.Equal(OpenHome.SceneKey, first.Synapse.SceneKey);
        Assert.Equal(SecondSceneKey, second.Synapse.SceneKey);
        Assert.True(second.Sequence > first.Sequence);
    }

    [Fact(DisplayName = "CountdownSurface is multi-module — Flutter shell scene + ICountdown schedule")]
    public async Task CountdownSurfaceComposesFlutterShellWithCountdown()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var test = await OSCluster.Fixture.CreateBrainAsync(cancellationToken);
        var shell = test.Neuron<IShell>(ShellName);
        var countdown = test.Neuron<ICountdown>(CountdownName);

        var started = await new CountdownSurface().RunAsync(
            test.Client,
            ShellName,
            CountdownName,
            CountdownDuration,
            cancellationToken);

        var opened = await shell.Outgoing.NextAsync<SceneOpened>(cancellationToken);
        Assert.Equal(CountdownSurface.SceneKey, opened.Synapse.SceneKey);
        Assert.Equal(CountdownStatus.Scheduled, started.Status);
        Assert.Equal(CountdownDuration, started.Duration);

        var reloaded = await countdown.Reference.Read();
        Assert.Equal(started.Generation, reloaded.Generation);
        Assert.Equal(started.Revision, reloaded.Revision);
    }

    [Fact(DisplayName = "AiPaneSurface is multi-module — Flutter shell scene + typed ILlama32 respond")]
    public async Task AiPaneSurfaceOpensSceneAndResponds()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var test = await OSCluster.Fixture.CreateBrainAsync(cancellationToken);
        var shell = test.Neuron<IShell>(ShellName);
        test.Chat().Reply(PaneReply);

        var response = await new AiPaneSurface().RunAsync(test.Client, ShellName, ModelName, PanePrompt, cancellationToken);

        var opened = await shell.Outgoing.NextAsync<SceneOpened>(cancellationToken);
        Assert.Equal(AiPaneSurface.SceneKey, opened.Synapse.SceneKey);
        Assert.Equal(PaneReply, response.Text);
    }
}
