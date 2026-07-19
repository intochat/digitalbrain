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

    [Then("the incoming journal of the {word} neuron named {string} contains {word}")]
    public Task ThenTheIncomingJournalContains(string neuronType, string name, string synapseType)
        => AssertJournalContains(JournalKind.Incoming, neuronType, name, synapseType);

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
