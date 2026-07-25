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
        var shell = test.Neuron<IShell>(FlutterFixture.ShellName);
        var command = new OpenScene(CommandId.New(), FlutterFixture.HomeSceneKey, FlutterFixture.HomeSceneTitle);

        await shell.Reference.Open(command);

        var opened = await shell.Outgoing.NextAsync<SceneOpened>(cancellationToken);
        Assert.Equal(command.CommandId, opened.Synapse.CommandId);
        Assert.Equal(shell.Id, opened.Synapse.Shell);
        Assert.Equal(command.SceneKey, opened.Synapse.SceneKey);
        Assert.Equal(command.Title, opened.Synapse.Title);
    }

    [Fact(DisplayName = "control activation is journaled on the scene as a directed fact")]
    public async Task ControlActivationIsJournaledOnTheScene()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var test = await fixture.CreateBrainAsync(cancellationToken);
        var scene = test.Neuron<IScene>(FlutterFixture.HomeSceneKey);
        var activation = new ControlActivated(
            FlutterFixture.HomeSceneKey, FlutterFixture.PrimaryControlId, FlutterFixture.SubmitIntent);

        await test.Client.SendAsync<IScene>(FlutterFixture.HomeSceneKey, activation);

        var received = await scene.Incoming.NextAsync<ControlActivated>(cancellationToken);
        Assert.Equal(activation.SceneKey, received.Synapse.SceneKey);
        Assert.Equal(activation.ControlId, received.Synapse.ControlId);
        Assert.Equal(activation.Intent, received.Synapse.Intent);
    }
}
