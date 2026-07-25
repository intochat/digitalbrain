using DigitalBrain.Abstractions;
using DigitalBrain.Testing;
using Xunit;

namespace DigitalBrain.Flutter.Tests;

public sealed class ShellSceneRoundTrip(FlutterFixture fixture)
{
    [Fact(DisplayName = "IShell.Open journals SceneOpened on the shell")]
    public async Task OpenJournalsSceneOpenedOnTheShell()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var test = await fixture.CreateBrainAsync(cancellationToken);
        var shell = test.Neuron<IShell>(FlutterFixture.ShellName);
        var command = new OpenScene(
            CommandId.New(),
            FlutterFixture.HomeSceneKey,
            FlutterFixture.HomeSceneTitle);

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
        var scene = test.Neuron<IScene>(FlutterFixture.HomeSceneKey);
        var activation = new ControlActivated(
            FlutterFixture.HomeSceneKey,
            FlutterFixture.PrimaryControlId,
            FlutterFixture.SubmitIntent);

        await test.Client.SendAsync<IScene>(FlutterFixture.HomeSceneKey, activation);

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
        var shell = test.Neuron<IShell>(FlutterFixture.ShellName);

        await shell.Reference.Open(new OpenScene(
            CommandId.New(),
            FlutterFixture.HomeSceneKey,
            FlutterFixture.HomeSceneTitle));

        var opened = await shell.Outgoing.NextAsync<SceneOpened>(cancellationToken);
        Assert.Equal(FlutterFixture.HomeSceneKey, opened.Synapse.SceneKey);

        Assert.Empty(await shell.Outgoing.ReadAsync<CapabilityRequested>(
            cancellationToken: cancellationToken));
        Assert.Empty(await shell.Outgoing.ReadAsync<CapabilityCompleted>(
            cancellationToken: cancellationToken));
        Assert.Empty(await shell.Outgoing.ReadAsync<CapabilityFailed>(
            cancellationToken: cancellationToken));
        Assert.Empty(await shell.Outgoing.ReadAsync<CapabilityRejected>(
            cancellationToken: cancellationToken));
        Assert.Empty(await shell.Incoming.ReadAsync<CapabilityRequested>(
            cancellationToken: cancellationToken));
        Assert.Empty(await shell.Incoming.ReadAsync<CapabilityCompleted>(
            cancellationToken: cancellationToken));
    }

    [Fact(DisplayName =
        "IShell.Open rejects blank SceneKey and Title without journaling SceneOpened")]
    public async Task OpenRejectsBlankSceneKeyAndTitle()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var test = await fixture.CreateBrainAsync(cancellationToken);
        var shell = test.Neuron<IShell>(FlutterFixture.ShellName);
        var commandId = CommandId.New();

        await Assert.ThrowsAsync<ArgumentException>(() =>
            shell.Reference.Open(new OpenScene(
                commandId,
                string.Empty,
                FlutterFixture.HomeSceneTitle)));
        await Assert.ThrowsAsync<ArgumentException>(() =>
            shell.Reference.Open(new OpenScene(
                commandId,
                "   ",
                FlutterFixture.HomeSceneTitle)));
        await Assert.ThrowsAsync<ArgumentException>(() =>
            shell.Reference.Open(new OpenScene(
                commandId,
                FlutterFixture.HomeSceneKey,
                string.Empty)));
        await Assert.ThrowsAsync<ArgumentException>(() =>
            shell.Reference.Open(new OpenScene(
                commandId,
                FlutterFixture.HomeSceneKey,
                "   ")));

        Assert.Empty(await shell.Outgoing.ReadAsync<SceneOpened>(
            cancellationToken: cancellationToken));
    }
}
