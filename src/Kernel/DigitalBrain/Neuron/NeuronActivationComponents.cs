using DigitalBrain.Abstractions.Signals;
using Orleans.Journaling;

namespace DigitalBrain.Core;

internal sealed record NeuronActivationComponents(
    TimeProvider Clock,
    NeuronOptions Options,
    NeuronJournals Journals,
    CommandJournal Commands,
    CommandDedup Dedup,
    CommandExecution Execution,
    NeuronSynapses Synapses,
    IDurableDictionary<string, SignalDelivery> Latest,
    PendingWork Pending)
{
    internal NeuronCommitBoundary CaptureCommitBoundary() => new(Journals.CaptureCommitBoundary(), Commands.CaptureCommitBoundary());

    internal void NoteCommitted(NeuronCommitBoundary boundary)
    {
        Journals.NoteCommitted(boundary.Journals);
        Commands.NoteCommitted(boundary.Commands);
    }

    internal void NoteReloaded()
    {
        Journals.NoteReloaded();
        Commands.NoteReloaded();
    }
}

internal readonly record struct NeuronCommitBoundary(NeuronJournalsBoundary Journals, long Commands);
