using DigitalBrain.Abstractions;

namespace DigitalBrain.Testing;

public sealed class TestNeuron<TNeuron>
    where TNeuron : class, INeuron
{
    internal TestNeuron(
        NeuronId id,
        TNeuron reference,
        TestJournal incoming,
        TestJournal outgoing)
    {
        Id = id;
        Reference = reference;
        Incoming = incoming;
        Outgoing = outgoing;
    }

    public NeuronId Id { get; }

    public TNeuron Reference { get; }

    public TestJournal Incoming { get; }

    public TestJournal Outgoing { get; }
}
