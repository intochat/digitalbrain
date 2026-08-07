using DigitalBrain.Abstractions;
using DigitalBrain.Shell;
using Xunit;

namespace DigitalBrain.OS.Bdd.Tests;

public sealed class ShellSceneRoundTrip
{
    private const string ShellName = "desk";
    private const string HomeSceneKey = "home";
    private const string HomeSceneTitle = "Home";
    private const string PrimaryControlId = "primary";
    private const string SubmitIntent = "submit";

    [Fact(DisplayName = "directed OpenScene journals SceneOpened on the shell")]
    public async Task OpenJournalsSceneOpenedOnTheShell()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var test = await OSCluster.Fixture.CreateBrainAsync(cancellationToken);
        var shell = test.Neuron<IShell>(ShellName);
        var command = new OpenScene(CommandId.New(), HomeSceneKey, HomeSceneTitle);

        await test.Client.SendAsync<IShell>(ShellName, command, cancellationToken);

        var opened = await shell.Outgoing.NextAsync<SceneOpened>(cancellationToken);
        Assert.Equal(command.CommandId, opened.Synapse.CommandId);
        Assert.Equal(shell.Id, opened.Synapse.Shell);
        Assert.Equal(command.SceneKey, opened.Synapse.SceneKey);
        Assert.Equal(command.Title, opened.Synapse.Title);
    }

    [Fact(DisplayName =
        "ControlActivated is journaled on IScene as a directed fact")]
    public async Task ControlActivatedIsJournaledOnTheScene()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var test = await OSCluster.Fixture.CreateBrainAsync(cancellationToken);
        var scene = test.Neuron<IScene>(HomeSceneKey);
        var activation = new ControlActivated(HomeSceneKey, PrimaryControlId, SubmitIntent);

        await test.Client.SendAsync<IScene>(HomeSceneKey, activation, cancellationToken);

        var received = await scene.Incoming.NextAsync<ControlActivated>(cancellationToken);
        Assert.Equal(activation.SceneKey, received.Synapse.SceneKey);
        Assert.Equal(activation.ControlId, received.Synapse.ControlId);
        Assert.Equal(activation.Intent, received.Synapse.Intent);
    }

    [Fact(DisplayName =
        "directed OpenScene produces SceneOpened with zero capability envelopes on shell journals")]
    public async Task OpenDoesNotJournalCapabilityFacts()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var test = await OSCluster.Fixture.CreateBrainAsync(cancellationToken);
        var shell = test.Neuron<IShell>(ShellName);

        await test.Client.SendAsync<IShell>(
            ShellName,
            new OpenScene(CommandId.New(), HomeSceneKey, HomeSceneTitle),
            cancellationToken);

        var opened = await shell.Outgoing.NextAsync<SceneOpened>(cancellationToken);
        Assert.Equal(HomeSceneKey, opened.Synapse.SceneKey);

        Assert.Empty(await shell.Outgoing.ReadAsync<CapabilityRequested>(cancellationToken: cancellationToken));
        Assert.Empty(await shell.Outgoing.ReadAsync<CapabilityCompleted>(cancellationToken: cancellationToken));
        Assert.Empty(await shell.Outgoing.ReadAsync<CapabilityFailed>(cancellationToken: cancellationToken));
        Assert.Empty(await shell.Outgoing.ReadAsync<CapabilityRejected>(cancellationToken: cancellationToken));
        Assert.Empty(await shell.Incoming.ReadAsync<CapabilityRequested>(cancellationToken: cancellationToken));
        Assert.Empty(await shell.Incoming.ReadAsync<CapabilityCompleted>(cancellationToken: cancellationToken));
    }

    [Fact(DisplayName =
        "blank SceneKey or Title is refused when minting OpenScene — synchronous ArgumentException at construction, not a Deliver retry storm")]
    public void OpenSceneRejectsBlankSceneKeyAndTitleAtMint()
    {
        var commandId = CommandId.New();

        Assert.Throws<ArgumentException>(() =>
            new OpenScene(commandId, string.Empty, HomeSceneTitle));
        Assert.Throws<ArgumentException>(() =>
            new OpenScene(commandId, "   ", HomeSceneTitle));
        Assert.Throws<ArgumentException>(() =>
            new OpenScene(commandId, HomeSceneKey, string.Empty));
        Assert.Throws<ArgumentException>(() =>
            new OpenScene(commandId, HomeSceneKey, "   "));
    }
}
