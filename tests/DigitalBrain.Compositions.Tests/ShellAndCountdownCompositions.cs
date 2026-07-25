using DigitalBrain.Flutter;
using DigitalBrain.Shell;
using DigitalBrain.Surfaces;
using DigitalBrain.Testing;
using DigitalBrain.Time;
using Xunit;

namespace DigitalBrain.Compositions.Tests;

public sealed class ShellAndCountdownCompositions(CompositionsFixture fixture)
{
    [Fact(DisplayName = "OpenHome composition journals SceneOpened without Kernel references")]
    public async Task OpenHomeCompositionJournalsSceneOpened()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var test = await fixture.CreateBrainAsync(cancellationToken);
        var shell = test.Neuron<IShell>("desk");

        await new OpenHome().RunAsync(
            test.Client,
            shellName: "desk",
            sceneKey: "home",
            title: "Home",
            cancellationToken);

        var opened = await shell.Outgoing.NextAsync<SceneOpened>(cancellationToken);
        Assert.Equal("home", opened.Synapse.SceneKey);
        Assert.Equal("Home", opened.Synapse.Title);
    }

    [Fact(DisplayName = "PostAuthBootstrap opens home after owner-bound client exists")]
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
        Assert.Equal("home", opened.Synapse.SceneKey);
    }

    [Fact(DisplayName = "CountdownSurface composes Flutter shell with ICountdown")]
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
        Assert.Equal("countdown", opened.Synapse.SceneKey);
        Assert.Equal(CountdownStatus.Scheduled, started.Status);
        Assert.Equal(TimeSpan.FromMinutes(5), started.Duration);

        var reloaded = await countdown.Reference.Read();
        Assert.Equal(started.Generation, reloaded.Generation);
        Assert.Equal(started.Revision, reloaded.Revision);
    }
}
