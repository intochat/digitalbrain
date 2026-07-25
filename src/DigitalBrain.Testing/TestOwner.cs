using DigitalBrain.Abstractions;
using DigitalBrain.Client;

namespace DigitalBrain.Testing;

public sealed class TestOwner
{
    private readonly TestBrain _brain;

    internal TestOwner(TestBrain brain, OwnerId id)
    {
        _brain = brain;
        Id = id;
        Client = DigitalBrainClient.Connect(brain.Cluster.Client, id.Value);
    }

    public OwnerId Id { get; }

    public IDigitalBrain Client { get; }

    public TestNeuron<TNeuron> Neuron<TNeuron>(string name = "default")
        where TNeuron : class, INeuron
    {
        try
        {
            var id = NeuronId.For<TNeuron>(Id, name);
            var reference = typeof(ISessionNeuron).IsAssignableFrom(typeof(TNeuron))
                ? _brain.Cluster.Client.GetGrain<TNeuron>(id.ToGrainId())
                : Client.Get<TNeuron>(name);
            return new TestNeuron<TNeuron>(
                _brain,
                id,
                reference,
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
