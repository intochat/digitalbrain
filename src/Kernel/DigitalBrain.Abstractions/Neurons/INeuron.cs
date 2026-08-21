using Orleans.Concurrency;

using DigitalBrain.Abstractions.Messaging;
using DigitalBrain.Abstractions.Journals;
namespace DigitalBrain.Abstractions.Neurons;

[Alias("DigitalBrain.Abstractions.INeuron")]
public interface INeuron : IGrainWithStringKey
{
    [Alias(nameof(Deliver))]
    [ResponseTimeout(NeuronCallTimeouts.LongRunning)]
    Task Deliver(SynapseDelivery delivery, CancellationToken cancellationToken = default);

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
