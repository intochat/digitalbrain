using DigitalBrain.Abstractions;
using DigitalBrain.Testing;
using Xunit;

namespace DigitalBrain.Flutter.Tests;

public sealed class ShellSceneRoundTrip(FlutterFixture fixture)
{
    [Fact(DisplayName = "opening a scene journals SceneOpened on the shell")]
    public async Task OpeningASceneJournalsSceneOpenedOnTheShell()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var test = await fixture.CreateBrainAsync(cancellationToken);
        var shell = test.Neuron<IShell>("desk");
        var command = new OpenScene(CommandId.New(), "home", "Home");

        await shell.Reference.Open(command);

        var opened = await shell.Outgoing.NextAsync<SceneOpened>(cancellationToken);
        Assert.Equal(command.CommandId, opened.Synapse.CommandId);
        Assert.Equal(shell.Id, opened.Synapse.Shell);
        Assert.Equal("home", opened.Synapse.SceneKey);
        Assert.Equal("Home", opened.Synapse.Title);
    }

    [Fact(DisplayName = "control activation is journaled on the scene as a directed fact")]
    public async Task ControlActivationIsJournaledOnTheScene()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var test = await fixture.CreateBrainAsync(cancellationToken);
        var scene = test.Neuron<IScene>("home");
        var activation = new ControlActivated("home", "primary", "submit");

        await test.Client.SendAsync<IScene>("home", activation);

        var received = await scene.Incoming.NextAsync<ControlActivated>(cancellationToken);
        Assert.Equal("home", received.Synapse.SceneKey);
        Assert.Equal("primary", received.Synapse.ControlId);
        Assert.Equal("submit", received.Synapse.Intent);
    }
}
