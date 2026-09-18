using DigitalBrain.Abstractions.Identity;
using DigitalBrain.Abstractions.Journals;
using DigitalBrain.Abstractions.Signals;
using Orleans.Journaling;

namespace DigitalBrain.Core;

internal sealed record NeuronActivationComponents(
    TimeProvider Clock,
    NeuronOptions Options,
    JournalWindow Incoming,
    JournalWindow Outgoing,
    CommandJournal Commands,
    CommandOutcomeStore CommandOutcomes,
    CommandExecution Execution,
    NeuronSynapses Synapses,
    IDurableDictionary<string, SignalDelivery> Latest,
    PendingWork Pending)
{
    internal long IncomingNextSequence => Incoming.NextSequence;

    internal long OutgoingNextSequence => Outgoing.NextSequence;

    internal JournalRead Read(JournalKind kind, long afterSequence) => kind switch
    {
        JournalKind.Incoming => Incoming.Read(afterSequence),
        JournalKind.Outgoing => Outgoing.Read(afterSequence),
        _ => throw new ArgumentOutOfRangeException(nameof(kind)),
    };

    internal void AppendIncoming(SignalDelivery delivery) => Incoming.Append(delivery);

    internal void AppendOutgoing(SignalDelivery delivery) => Outgoing.Append(delivery);

    internal bool RetainsOutgoing(SignalId signalId) => Outgoing.Retains(signalId);

    internal NeuronCommitBoundary CaptureCommitBoundary()
        => new(Incoming.CaptureCommitBoundary(), Outgoing.CaptureCommitBoundary(), Commands.CaptureCommitBoundary());

    internal void NoteCommitted(NeuronCommitBoundary boundary)
    {
        Incoming.NoteCommitted(boundary.Incoming);
        Outgoing.NoteCommitted(boundary.Outgoing);
        Commands.NoteCommitted(boundary.Commands);
    }

    internal void NoteReloaded()
    {
        Incoming.NoteReloaded();
        Outgoing.NoteReloaded();
        Commands.NoteReloaded();
        CommandOutcomes.NoteReloaded();
        Pending.NoteReloaded();
    }
}

internal readonly record struct NeuronCommitBoundary(
    JournalWindowBoundary Incoming, JournalWindowBoundary Outgoing, JournalCommitBoundary Commands);
