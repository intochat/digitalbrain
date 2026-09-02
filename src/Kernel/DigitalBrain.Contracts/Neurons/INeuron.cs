using Orleans.Concurrency;

using DigitalBrain.Abstractions.Signals;
using DigitalBrain.Abstractions.Journals;
using DigitalBrain.Abstractions.Synapses;
namespace DigitalBrain.Abstractions.Neurons;

[Alias("DigitalBrain.Abstractions.INeuron")]
public interface INeuron : IGrainWithStringKey
{
    [Alias(nameof(Deliver))]
    [ResponseTimeout(NeuronCallTimeouts.LongRunning)]
    Task Deliver(SignalDelivery delivery, CancellationToken cancellationToken = default);

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

    // The neuron's own outgoing edges. Free: no journal entry, no correlation, decay applied
    // at read. This is the query the graph UI and the console proof both use.
    [ReadOnly]
    [AlwaysInterleave]
    [Alias(nameof(ReadSynapses))]
    Task<IReadOnlyList<Synapse>> ReadSynapses();
}
