using DigitalBrain.Abstractions;
using DigitalBrain.Flutter;
using DigitalBrain.Shell;
using DigitalBrain.Testing;
using Xunit;

namespace DigitalBrain.Compositions.Tests;

public sealed class BehaviorOsActivationBoot(CompositionsFixture fixture)
{
    private const string ShellName = "desk";

    [Fact(DisplayName =
        "Given DigitalBrain is activated for an owner; When DigitalBrainActivated is committed; Then an OS behavior/composition reacts and the UI starts via IShell")]
    public async Task ActivationSynapseDrivesOsBehaviorToStartUi()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var test = await fixture.CreateBrainAsync(cancellationToken);
        var shell = test.Neuron<IShell>(ShellName);
        var session = test.Neuron<ISessionNeuron>(ISessionNeuron.InstanceName);

        await new ActivateDigitalBrain().RunAsync(test.Client, cancellationToken);
        await new BootOnActivation().RunAsync(test.Client, ShellName, cancellationToken);

        var activated = await session.Outgoing.NextAsync<DigitalBrainActivated>(cancellationToken);
        Assert.Equal(test.Client.Owner, activated.Synapse.Owner);

        var opened = await shell.Outgoing.NextAsync<SceneOpened>(cancellationToken);
        Assert.Equal(OpenHome.SceneKey, opened.Synapse.SceneKey);
        Assert.Equal(OpenHome.SceneTitle, opened.Synapse.Title);
    }

    [Fact(DisplayName =
        "When DigitalBrainActivated is committed, SceneOpened for home first screen is presented (journal evidence)")]
    public async Task ActivationCommittedObservesSceneOpenedHome()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var test = await fixture.CreateBrainAsync(cancellationToken);
        var shell = test.Neuron<IShell>(ShellName);

        await new ActivateDigitalBrain().RunAsync(test.Client, cancellationToken);
        await new BootOnActivation().RunAsync(test.Client, ShellName, cancellationToken);

        var opened = await shell.Outgoing.NextAsync<SceneOpened>(cancellationToken);
        Assert.Equal(OpenHome.SceneKey, opened.Synapse.SceneKey);
        Assert.Equal(OpenHome.SceneTitle, opened.Synapse.Title);
        Assert.Equal("home", opened.Synapse.SceneKey);
        Assert.Equal("Home", opened.Synapse.Title);
    }
}
