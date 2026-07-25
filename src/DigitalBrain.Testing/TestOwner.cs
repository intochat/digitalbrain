using DigitalBrain.Abstractions;
using DigitalBrain.Client;

namespace DigitalBrain.Testing;

public sealed class TestOwner
{
    private readonly TestBrain _brain;
    private readonly OwnerId _id;

    internal TestOwner(TestBrain brain, OwnerId id)
    {
        _brain = brain;
        _id = id;
        Client = DigitalBrainClient.Connect(brain.Cluster.Client, id.Value);
    }

    internal IDigitalBrain Client { get; }

    public TestNeuron<TNeuron> Neuron<TNeuron>(string name = "default")
        where TNeuron : class, INeuron
    {
        try
        {
            var id = NeuronId.For<TNeuron>(_id, name);
            return new TestNeuron<TNeuron>(
                _brain,
                id,
                Client.Get<TNeuron>(name),
                _brain.Journal(id, JournalKind.Incoming),
                _brain.Journal(id, JournalKind.Outgoing));
        }
        catch (Exception failure)
            when (failure is not BrainTestFailureException)
        {
            throw _brain.CaptureFailure(
                "neuron.open",
                failure);
        }
    }
}
