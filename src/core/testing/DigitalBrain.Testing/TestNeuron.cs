using DigitalBrain.Abstractions;

namespace DigitalBrain.Testing;

public sealed class TestNeuron<TNeuron>
    where TNeuron : class, INeuron
{
    private readonly TestBrain _brain;

    internal TestNeuron(TestBrain brain, NeuronId id, TNeuron reference, TestJournal incoming, TestJournal outgoing)
    {
        _brain = brain;
        Id = id;
        Reference = reference;
        Incoming = incoming;
        Outgoing = outgoing;
    }

    public NeuronId Id { get; }

    public TNeuron Reference { get; }

    public TestJournal Incoming { get; }

    public TestJournal Outgoing { get; }

    public JournalFaultHandle FailNextJournalCommit(string message)
        => _brain.ArmJournalFault(Id, message);

    public JournalFaultHandle FailJournalCommitAfter(int allowCommitsBeforeFault, string message)
        => _brain.ArmJournalFault(Id, message, allowCommitsBeforeFault);

    public Task<bool> HasOutboxWakeupAsync()
        => _brain.HasOutboxWakeupAsync(Id);

    public Task RestartHostAsync(CancellationToken cancellationToken = default)
        => _brain.RestartHostAsync(Id, cancellationToken);
}
