using Reqnroll;

namespace DigitalBrain.Testing;

[Binding]
public sealed class NeuronSteps(Simulation simulation)
{
    [BeforeTestRun]
    public static Task StartCluster() => SimulationCluster.StartAsync();

    [AfterTestRun]
    public static Task StopCluster() => SimulationCluster.StopAsync();

    [Given("a brain for owner {string}")]
    public void GivenABrainForOwner(string owner) => simulation.OpenBrain(owner);

    [When("{word} is sent to the {word} neuron named {string}")]
    public Task WhenSynapseIsSentToNeuron(string synapseType, string neuronType, string name)
        => simulation.SendAsync(synapseType, neuronType, name, new Dictionary<string, string>(StringComparer.Ordinal));

    [When("{word} is sent to the {word} neuron named {string} with")]
    public Task WhenSynapseIsSentToNeuronWith(string synapseType, string neuronType, string name, DataTable values)
    {
        ArgumentNullException.ThrowIfNull(values);

        return simulation.SendAsync(
            synapseType,
            neuronType,
            name,
            values.Rows.ToDictionary(row => row[0], row => row[1], StringComparer.Ordinal));
    }

    [When("{word} is sent twice to the {word} neuron named {string}")]
    public Task WhenTheSameSynapseIsSentTwice(string synapseType, string neuronType, string name)
        => simulation.SendTwiceAsync(synapseType, neuronType, name);

    [Then("the {word} journal of the {word} neuron named {string} contains {word} exactly once")]
    public async Task ThenTheJournalContainsExactlyOnce(string journal, string neuronType, string name, string synapseType)
    {
        var kind = Enum.Parse<JournalKind>(journal, ignoreCase: true);
        var recorded = await simulation.ReadJournalAsync(kind, neuronType, name);
        var expected = NeuronCatalog.SynapseType(synapseType);
        var occurrences = recorded.Count(expected.IsInstanceOfType);

        if (occurrences != 1)
        {
            throw new SimulationAssertionException(
                $"Expected exactly one {synapseType} in the {kind} journal of {neuronType} '{name}', but found {occurrences}.");
        }
    }

    [When("{word} is sent to the {word} neuron named {string} claiming owner {string}")]
    public Task WhenSynapseIsSentClaimingAnotherOwner(string synapseType, string neuronType, string name, string claimedOwner)
        => simulation.SendClaimingOwnerAsync(synapseType, neuronType, name, claimedOwner);

    [Then("the synapse is refused as unauthorized")]
    public void ThenTheSynapseIsRefusedAsUnauthorized() => simulation.ExpectRefusal<NeuronAuthorizationException>();

    [Then("the incoming journal of the {word} neuron named {string} is empty")]
    public async Task ThenTheIncomingJournalIsEmpty(string neuronType, string name)
    {
        var journal = await simulation.ReadJournalAsync(JournalKind.Incoming, neuronType, name);

        if (journal.Count > 0)
        {
            throw new SimulationAssertionException(
                $"Expected the incoming journal of {neuronType} '{name}' to be empty, but it recorded {string.Join(", ", journal.Select(synapse => synapse.GetType().Name))}.");
        }
    }

    [Given("a {word} neuron named {string} is registered")]
    [When("a {word} neuron named {string} is registered")]
    public Task ANeuronIsRegistered(string neuronType, string name) => simulation.RegisterAsync(neuronType, name);

    [When("the cluster is restarted")]
    public static Task WhenTheClusterIsRestarted() => SimulationCluster.RestartAsync();

    [Then("the subscriber count for {word} has grown by {int}")]
    public async Task ThenTheSubscriberCountHasGrownBy(string synapseType, int expected)
    {
        var actual = await simulation.SubscriberCountAsync(synapseType);

        if (actual != expected)
        {
            throw new SimulationAssertionException(
                $"Expected the subscriber count for {synapseType} to have grown by {expected}, but it is {actual}.");
        }
    }

    [Then("the incoming journal of the {word} neuron named {string} contains {word}")]
    public async Task ThenTheIncomingJournalContains(string neuronType, string name, string synapseType)
    {
        await simulation.AwaitHandledAsync(neuronType, name, synapseType);
        await AssertJournalContains(JournalKind.Incoming, neuronType, name, synapseType);
    }

    [Then("the outgoing journal of the {word} neuron named {string} contains {word}")]
    public Task ThenTheOutgoingJournalContains(string neuronType, string name, string synapseType)
        => AssertJournalContains(JournalKind.Outgoing, neuronType, name, synapseType);

    private async Task AssertJournalContains(JournalKind kind, string neuronType, string name, string synapseType)
    {
        var journal = await simulation.ReadJournalAsync(kind, neuronType, name);
        var expected = NeuronCatalog.SynapseType(synapseType);

        if (!journal.Any(expected.IsInstanceOfType))
        {
            var recorded = journal.Count == 0 ? "nothing" : string.Join(", ", journal.Select(synapse => synapse.GetType().Name));

            throw new SimulationAssertionException(
                $"Expected the {kind} journal of {neuronType} '{name}' to contain {synapseType}, but it recorded {recorded}.");
        }
    }
}
