using DigitalBrain.Abstractions.Journals;
using DigitalBrain.Abstractions.Signals;

namespace DigitalBrain.Core;

internal sealed class NeuronJournals(JournalWindow incoming, JournalWindow outgoing)
{
    internal long OutgoingNextSequence => outgoing.NextSequence;

    internal long IncomingNextSequence => incoming.NextSequence;

    internal JournalRead Read(JournalKind kind, long afterSequence) => WindowFor(kind).Read(afterSequence);

    internal void AppendIncoming(SignalDelivery delivery) => incoming.Append(delivery);

    internal void AppendOutgoing(SignalDelivery delivery) => outgoing.Append(delivery);

    internal NeuronJournalsBoundary CaptureCommitBoundary() => new(incoming.CaptureCommitBoundary(), outgoing.CaptureCommitBoundary());

    internal void NoteCommitted(NeuronJournalsBoundary boundary)
    {
        incoming.NoteCommitted(boundary.Incoming);
        outgoing.NoteCommitted(boundary.Outgoing);
    }

    internal void NoteReloaded()
    {
        incoming.NoteReloaded();
        outgoing.NoteReloaded();
    }

    private JournalWindow WindowFor(JournalKind kind) => kind switch
    {
        JournalKind.Incoming => incoming,
        JournalKind.Outgoing => outgoing,
        _ => throw new ArgumentOutOfRangeException(nameof(kind)),
    };
}

internal readonly record struct NeuronJournalsBoundary(JournalWindowBoundary Incoming, JournalWindowBoundary Outgoing);
