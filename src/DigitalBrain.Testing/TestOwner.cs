using DigitalBrain.Abstractions;
using DigitalBrain.Client;

namespace DigitalBrain.Testing;

public sealed class TestOwner
{
    private readonly TestBrain _brain;

    private TestOwner(
        TestBrain brain,
        OwnerId id,
        IDigitalBrain client)
    {
        _brain = brain;
        Id = id;
        Client = client;
    }

    public OwnerId Id { get; }

    public IDigitalBrain Client { get; }

    public TestNeuron<TNeuron> Neuron<TNeuron>(string name = "default")
        where TNeuron : class, INeuron
    {
        var id = NeuronId.For<TNeuron>(Id, name);

        return new(
            id,
            Client.Get<TNeuron>(name),
            _brain.Journal(id, JournalKind.Incoming),
            _brain.Journal(id, JournalKind.Outgoing));
    }

    internal static TestOwner Create(TestBrain brain, OwnerId id)
        => new(
            brain,
            id,
            DigitalBrainClient.Connect(brain.Cluster.Client, id.Value));
}

internal static class IdentityLabel
{
    internal static string Validate(string label)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(label);

        if (label.Contains('/', StringComparison.Ordinal))
        {
            throw new ArgumentException(
                "Owner labels cannot contain '/'.",
                nameof(label));
        }

        if (label.Any(char.IsWhiteSpace))
        {
            throw new ArgumentException(
                "Owner labels cannot contain whitespace.",
                nameof(label));
        }

        return label;
    }
}
