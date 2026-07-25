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
        "Given DigitalBrain is activated for an owner; When DigitalBrainActivated is committed by the brain neuron; Then first Behavior opens home via IShell")]
    public async Task ActivationSynapseDrivesOsBehaviorToStartUi()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var test = await fixture.CreateBrainAsync(cancellationToken);
        var shell = test.Neuron<IShell>(ShellName);
        var brain = test.Neuron<IDigitalBrainNeuron>(IDigitalBrainNeuron.InstanceName);

        await test.Client.ActivateAsync();

        var activated = await brain.Outgoing.NextAsync<DigitalBrainActivated>(cancellationToken);
        Assert.Equal(test.Client.Owner, activated.Synapse.Owner);

        var opened = await shell.Outgoing.NextAsync<SceneOpened>(cancellationToken);
        Assert.Equal(OpenHome.SceneKey, opened.Synapse.SceneKey);
        Assert.Equal(OpenHome.SceneTitle, opened.Synapse.Title);
    }

    [Fact(DisplayName =
        "When DigitalBrainActivated is committed, SceneOpened for home first screen is presented (journal evidence) without pull BootOnActivation")]
    public async Task ActivationCommittedObservesSceneOpenedHome()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var test = await fixture.CreateBrainAsync(cancellationToken);
        var shell = test.Neuron<IShell>(ShellName);

        await new ActivateDigitalBrain().RunAsync(test.Client, cancellationToken);

        var opened = await shell.Outgoing.NextAsync<SceneOpened>(cancellationToken);
        Assert.Equal(OpenHome.SceneKey, opened.Synapse.SceneKey);
        Assert.Equal(OpenHome.SceneTitle, opened.Synapse.Title);
        Assert.Equal("home", opened.Synapse.SceneKey);
        Assert.Equal("Home", opened.Synapse.Title);
    }

    [Fact(DisplayName =
        "DigitalBrain neuron Activate is idempotent — second Activate does not re-open home")]
    public async Task SecondActivateDoesNotEmitAgain()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var test = await fixture.CreateBrainAsync(cancellationToken);
        var shell = test.Neuron<IShell>(ShellName);
        var brain = test.Neuron<IDigitalBrainNeuron>(IDigitalBrainNeuron.InstanceName);

        await test.Client.ActivateAsync();
        var first = await brain.Outgoing.NextAsync<DigitalBrainActivated>(cancellationToken);
        await shell.Outgoing.NextAsync<SceneOpened>(cancellationToken);

        await test.Client.ActivateAsync();

        var activations = await brain.Outgoing.ReadAsync<DigitalBrainActivated>(
            afterSequence: 0,
            cancellationToken);
        Assert.Single(activations);
        Assert.Equal(first.SynapseId, activations[0].SynapseId);
    }
}
