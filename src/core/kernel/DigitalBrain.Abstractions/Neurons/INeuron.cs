using Orleans.Concurrency;

namespace DigitalBrain.Abstractions;

[Alias("DigitalBrain.Abstractions.INeuron")]
public interface INeuron : IGrainWithStringKey
{
    [Alias(nameof(Deliver))]
    [ResponseTimeout(NeuronCallTimeouts.LongRunning)]
    Task Deliver(SynapseDelivery delivery, CancellationToken cancellationToken = default);

    // The only interleaving surface a neuron has, and what lets a conversation be asked about itself
    // while the turn asking is still open. Reading a feed neither yields nor writes, and appends and
    // flushes are synchronous, so an interleaved read never sees a torn feed: entries, sequence and
    // tallies always agree. It CAN see a turn that has been appended but not yet committed - the
    // window between FlushOutgoing and the CommitAsync await - which a failed commit then discards by
    // restoring the turn checkpoint. Closing that needs a committed watermark on the feed, not an
    // attribute here.
    [ReadOnly]
    [AlwaysInterleave]
    [Alias(nameof(ReadJournal))]
    [ResponseTimeout(NeuronCallTimeouts.LongRunning)]
    Task<JournalRead> ReadJournal(JournalKind kind, long afterSequence);

    [Alias(nameof(Watch))]
    [ResponseTimeout(NeuronCallTimeouts.LongRunning)]
    Task Watch(JournalKind kind, long afterSequence, IJournalObserver observer);

    [Alias(nameof(Unwatch))]
    Task Unwatch(IJournalObserver observer);
}
