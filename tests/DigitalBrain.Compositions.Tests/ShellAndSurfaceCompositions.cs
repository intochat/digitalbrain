using DigitalBrain.Flutter;
using DigitalBrain.Shell;
using DigitalBrain.Surfaces;
using DigitalBrain.Testing;
using DigitalBrain.Time;
using Xunit;

namespace DigitalBrain.Compositions.Tests;

public sealed class ShellAndSurfaceCompositions(CompositionsFixture fixture)
{
    [Fact(DisplayName = "OpenHome is shell-only — journals SceneOpened for the home scene only")]
    public async Task OpenHomeCompositionJournalsSceneOpened()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var test = await fixture.CreateBrainAsync(cancellationToken);
        var shell = test.Neuron<IShell>("desk");

        await new OpenHome().RunAsync(
            test.Client,
            shellName: "desk",
            cancellationToken);

        var opened = await shell.Outgoing.NextAsync<SceneOpened>(cancellationToken);
        Assert.Equal(OpenHome.SceneKey, opened.Synapse.SceneKey);
        Assert.Equal(OpenHome.SceneTitle, opened.Synapse.Title);
    }

    [Fact(DisplayName = "PostAuthBootstrap is shell-only — opens home via IShell (not peer composition)")]
    public async Task PostAuthBootstrapOpensHome()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var test = await fixture.CreateBrainAsync(cancellationToken);
        var shell = test.Neuron<IShell>("desk");

        await new PostAuthBootstrap().RunAsync(
            test.Client,
            shellName: "desk",
            cancellationToken);

        var opened = await shell.Outgoing.NextAsync<SceneOpened>(cancellationToken);
        Assert.Equal(OpenHome.SceneKey, opened.Synapse.SceneKey);
        Assert.Equal(OpenHome.SceneTitle, opened.Synapse.Title);
    }

    [Fact(DisplayName = "NavigateShell is shell-only — journals multiple SceneOpened facts in order")]
    public async Task NavigateShellJournalsMultipleScenes()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var test = await fixture.CreateBrainAsync(cancellationToken);
        var shell = test.Neuron<IShell>("desk");

        await new NavigateShell().RunAsync(
            test.Client,
            shellName: "desk",
            scenes:
            [
                ("home", "Home"),
                ("settings", "Settings"),
            ],
            cancellationToken);

        var first = await shell.Outgoing.NextAsync<SceneOpened>(cancellationToken);
        var second = await shell.Outgoing.NextAsync<SceneOpened>(cancellationToken);
        Assert.Equal("home", first.Synapse.SceneKey);
        Assert.Equal("settings", second.Synapse.SceneKey);
        Assert.True(second.Sequence > first.Sequence);
    }

    [Fact(DisplayName = "CountdownSurface is multi-module — Flutter shell scene + ICountdown schedule")]
    public async Task CountdownSurfaceComposesFlutterShellWithCountdown()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var test = await fixture.CreateBrainAsync(cancellationToken);
        var shell = test.Neuron<IShell>("desk");
        var countdown = test.Neuron<ICountdown>("timer");

        var started = await new CountdownSurface().RunAsync(
            test.Client,
            shellName: "desk",
            countdownName: "timer",
            duration: TimeSpan.FromMinutes(5),
            cancellationToken);

        var opened = await shell.Outgoing.NextAsync<SceneOpened>(cancellationToken);
        Assert.Equal(CountdownSurface.SceneKey, opened.Synapse.SceneKey);
        Assert.Equal(CountdownStatus.Scheduled, started.Status);
        Assert.Equal(TimeSpan.FromMinutes(5), started.Duration);

        var reloaded = await countdown.Reference.Read();
        Assert.Equal(started.Generation, reloaded.Generation);
        Assert.Equal(started.Revision, reloaded.Revision);
    }

    [Fact(DisplayName =
        "AccountEnrichmentSurface is OS-scene-only — opens enrichment scene; not multi-module process")]
    public async Task AccountEnrichmentSurfaceOpensEnrichmentSceneOnly()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var test = await fixture.CreateBrainAsync(cancellationToken);
        var shell = test.Neuron<IShell>("desk");

        await new AccountEnrichmentSurface().RunAsync(
            test.Client,
            shellName: "desk",
            cancellationToken);

        var opened = await shell.Outgoing.NextAsync<SceneOpened>(cancellationToken);
        Assert.Equal(AccountEnrichmentSurface.SceneKey, opened.Synapse.SceneKey);
        Assert.Equal(AccountEnrichmentSurface.SceneTitle, opened.Synapse.Title);
        Assert.DoesNotContain("token", opened.Synapse.Title, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("password", opened.Synapse.Title, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("secret", opened.Synapse.Title, StringComparison.OrdinalIgnoreCase);
    }

    [Fact(DisplayName = "AiPaneSurface is multi-module — Flutter shell scene + typed ILlama32 respond")]
    public async Task AiPaneSurfaceOpensSceneAndResponds()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var test = await fixture.CreateBrainAsync(cancellationToken);
        var shell = test.Neuron<IShell>("desk");
        test.Chat().Reply("hello from pane");

        var response = await new AiPaneSurface().RunAsync(
            test.Client,
            shellName: "desk",
            modelName: "assistant",
            prompt: "ping",
            cancellationToken);

        var opened = await shell.Outgoing.NextAsync<SceneOpened>(cancellationToken);
        Assert.Equal(AiPaneSurface.SceneKey, opened.Synapse.SceneKey);
        Assert.Equal("hello from pane", response.Text);
    }
}
