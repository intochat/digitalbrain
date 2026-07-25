using DigitalBrain.Abstractions;
using DigitalBrain.Flutter;
using DigitalBrain.Testing;
using Reqnroll;
using Xunit;

namespace DigitalBrain.Compositions.Tests.Features;

[Binding]
public sealed class DigitalBrainActivationBindings
{
    private TestBrain? _brain;
    private TestNeuron<IShell>? _shell;
    private TestNeuron<IDigitalBrainNeuron>? _digitalBrain;
    private ObservedSynapse<DigitalBrainActivated>? _activation;
    private ObservedSynapse<SceneOpened>? _sceneOpened;

    [AfterScenario]
    public async Task DisposeBrain()
    {
        _shell = null;
        _digitalBrain = null;
        _activation = null;
        _sceneOpened = null;

        if (Interlocked.Exchange(ref _brain, null) is { } brain)
        {
            await brain.DisposeAsync();
        }
    }

    [Given("a DigitalBrain for the default owner")]
    public async Task GivenADigitalBrainForTheDefaultOwner()
    {
        if (_brain is not null)
        {
            throw new InvalidOperationException(
                "A scenario opens exactly one method-scoped TestBrain.");
        }

        var fixture = await TestContext.Current.GetFixture<CompositionsFixture>()
            ?? throw new InvalidOperationException(
                "The assembly-owned CompositionsFixture is unavailable.");

        _brain = await fixture.CreateBrainAsync(TestContext.Current.CancellationToken);
        _digitalBrain = _brain.Neuron<IDigitalBrainNeuron>(IDigitalBrainNeuron.InstanceName);
    }

    [Given(@"the shell neuron named ""(.*)""")]
    public void GivenTheShellNeuronNamed(string shellName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(shellName);
        _shell = Brain().Neuron<IShell>(shellName);
    }

    [When("the owner activates DigitalBrain")]
    [When("the owner activates DigitalBrain again")]
    public Task WhenTheOwnerActivatesDigitalBrain()
        => Brain().Client.ActivateAsync();

    [Then("the DigitalBrain neuron outgoing journal contains DigitalBrainActivated for the owner")]
    public async Task ThenDigitalBrainOutgoingContainsActivation()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        _activation = await DigitalBrainNeuron().Outgoing
            .NextAsync<DigitalBrainActivated>(cancellationToken);

        Assert.Equal(Brain().Client.Owner, _activation.Synapse.Owner);
    }

    [Then(
        @"the shell neuron ""(.*)"" outgoing journal contains SceneOpened with sceneKey ""(.*)"" and title ""(.*)""")]
    public async Task ThenShellOutgoingContainsSceneOpened(
        string shellName,
        string sceneKey,
        string title)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(shellName);
        ArgumentException.ThrowIfNullOrWhiteSpace(sceneKey);
        ArgumentException.ThrowIfNullOrWhiteSpace(title);

        var shell = _shell ?? Brain().Neuron<IShell>(shellName);
        _sceneOpened = await shell.Outgoing.NextAsync<SceneOpened>(
            TestContext.Current.CancellationToken);

        Assert.Equal(sceneKey, _sceneOpened.Synapse.SceneKey);
        Assert.Equal(title, _sceneOpened.Synapse.Title);
    }

    [Then("the DigitalBrain neuron outgoing journal has exactly {int} DigitalBrainActivated")]
    public async Task ThenDigitalBrainOutgoingHasExactActivationCount(int expected)
    {
        var activations = await DigitalBrainNeuron().Outgoing.ReadAsync<DigitalBrainActivated>(
            afterSequence: 0,
            TestContext.Current.CancellationToken);

        Assert.Equal(expected, activations.Count);
        if (_activation is not null && expected >= 1)
        {
            Assert.Equal(_activation.SynapseId, activations[0].SynapseId);
        }
    }

    private TestBrain Brain()
        => _brain
            ?? throw new InvalidOperationException(
                "Open a DigitalBrain with 'Given a DigitalBrain for the default owner' first.");

    private TestNeuron<IDigitalBrainNeuron> DigitalBrainNeuron()
        => _digitalBrain
            ?? throw new InvalidOperationException(
                "DigitalBrain neuron was not opened for this scenario.");
}
