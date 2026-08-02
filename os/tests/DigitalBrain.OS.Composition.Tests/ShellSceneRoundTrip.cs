using DigitalBrain.Abstractions;
using DigitalBrain.Flutter;
using Xunit;

namespace DigitalBrain.Compositions.Tests;

public sealed class ShellSceneRoundTrip(CompositionsFixture fixture)
{
    private const string ShellName = "desk";
    private const string HomeSceneKey = "home";
    private const string HomeSceneTitle = "Home";
    private const string PrimaryControlId = "primary";
    private const string SubmitIntent = "submit";

    [Fact(DisplayName = "IShell.Open journals SceneOpened on the shell")]
    public async Task OpenJournalsSceneOpenedOnTheShell()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var test = await fixture.CreateBrainAsync(cancellationToken);
        var shell = test.Neuron<IShell>(ShellName);
        var command = new OpenScene(CommandId.New(), HomeSceneKey, HomeSceneTitle);

        await shell.Reference.Open(command);

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
        await using var test = await fixture.CreateBrainAsync(cancellationToken);
        var scene = test.Neuron<IScene>(HomeSceneKey);
        var activation = new ControlActivated(HomeSceneKey, PrimaryControlId, SubmitIntent);

        await test.Client.SendAsync<IScene>(HomeSceneKey, activation, cancellationToken);

        var received = await scene.Incoming.NextAsync<ControlActivated>(cancellationToken);
        Assert.Equal(activation.SceneKey, received.Synapse.SceneKey);
        Assert.Equal(activation.ControlId, received.Synapse.ControlId);
        Assert.Equal(activation.Intent, received.Synapse.Intent);
    }

    [Fact(DisplayName =
        "ClientEntryPoint IShell.Open from an unattributed client does not journal capability facts")]
    public async Task OpenDoesNotJournalCapabilityFacts()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var test = await fixture.CreateBrainAsync(cancellationToken);
        var shell = test.Neuron<IShell>(ShellName);

        await shell.Reference.Open(new OpenScene(CommandId.New(), HomeSceneKey, HomeSceneTitle));

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
        "IShell.Open rejects blank SceneKey and Title without journaling SceneOpened")]
    public async Task OpenRejectsBlankSceneKeyAndTitle()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var test = await fixture.CreateBrainAsync(cancellationToken);
        var shell = test.Neuron<IShell>(ShellName);
        var commandId = CommandId.New();

        await Assert.ThrowsAsync<ArgumentException>(() =>
            shell.Reference.Open(new OpenScene(commandId, string.Empty, HomeSceneTitle)));
        await Assert.ThrowsAsync<ArgumentException>(() =>
            shell.Reference.Open(new OpenScene(commandId, "   ", HomeSceneTitle)));
        await Assert.ThrowsAsync<ArgumentException>(() =>
            shell.Reference.Open(new OpenScene(commandId, HomeSceneKey, string.Empty)));
        await Assert.ThrowsAsync<ArgumentException>(() =>
            shell.Reference.Open(new OpenScene(commandId, HomeSceneKey, "   ")));

        Assert.Empty(await shell.Outgoing.ReadAsync<SceneOpened>(cancellationToken: cancellationToken));
    }
}
