using Orleans.Concurrency;

namespace DigitalBrain.Abstractions;

public partial interface INeuron : IGrainWithStringKey
{
    [Alias(nameof(Deliver))]
    [ResponseTimeout(NeuronCallTimeouts.LongRunning)]
    Task Deliver(SynapseDelivery delivery, CancellationToken cancellationToken = default);

    // The only interleaving surface a neuron has. Reading a feed neither yields nor writes, so it
    // runs to completion inside one scheduler slot and cannot observe a half-applied turn; that is
    // what lets a conversation be asked about itself while the turn asking is still open.
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
