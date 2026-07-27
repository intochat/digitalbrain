using DigitalBrain.Abstractions;
using DigitalBrain.Flutter;
using Reqnroll;
using Xunit;

namespace DigitalBrain.Os.Bdd.Tests;

[Binding]
public sealed class ActivationSteps(BrainWorld world)
{
    [Given("a DigitalBrain for the default owner")]
    public Task GivenADigitalBrain() => world.OpenAsync();

    [Given("the shell {string} is observed")]
    public void GivenTheShellIsObserved(string shellName)
        => _ = world.Neuron<IShell>(shellName);

    [When("the owner activates DigitalBrain")]
    [When("the owner activates DigitalBrain again")]
    public Task WhenTheOwnerActivates() => world.Brain.Client.ActivateAsync();

    [Then("the DigitalBrain neuron journals DigitalBrainActivated for that owner")]
    public async Task ThenActivationIsJournaled()
    {
        var brain = world.Neuron<IDigitalBrainNeuron>(IDigitalBrainNeuron.InstanceName);

        var activated = await brain.Outgoing.NextAsync<DigitalBrainActivated>(
            BrainWorld.CancellationToken);

        Assert.Equal(world.Brain.Client.Owner, activated.Synapse.Owner);
    }

    [Then("the shell {string} journals SceneOpened for scene {string} titled {string}")]
    public async Task ThenTheShellJournalsSceneOpened(string shellName, string sceneKey, string title)
    {
        var shell = world.Neuron<IShell>(shellName);

        var opened = await shell.Outgoing.NextAsync<SceneOpened>(BrainWorld.CancellationToken);

        Assert.Equal(sceneKey, opened.Synapse.SceneKey);
        Assert.Equal(title, opened.Synapse.Title);
    }

    [Then("the DigitalBrain neuron has journaled DigitalBrainActivated exactly {int} time")]
    public async Task ThenActivationCountIs(int expected)
    {
        var brain = world.Neuron<IDigitalBrainNeuron>(IDigitalBrainNeuron.InstanceName);

        var activations = await brain.Outgoing.ReadAsync<DigitalBrainActivated>(
            afterSequence: 0,
            BrainWorld.CancellationToken);

        Assert.Equal(expected, activations.Count);
    }
}
