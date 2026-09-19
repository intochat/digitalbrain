using DigitalBrain.Abstractions.Identity;
using DigitalBrain.Abstractions.Journals;
using DigitalBrain.Abstractions.Signals;
using Orleans.Concurrency;

namespace DigitalBrain.Abstractions.Neurons;

// A named object: it emits facts, it receives facts, it answers reads.
// Synapses and routing live on IScenario, not here.
[Alias("db.v3.neuron")]
public interface INeuron : IGrainWithStringKey
{
    [Alias(nameof(Fire))]
    [ResponseTimeout(NeuronCallTimeouts.LongRunning)]
    Task<FireOutcome> Fire(Signal signal, CorrelationId? correlation = null, CancellationToken cancellationToken = default);

    [Alias(nameof(Deliver))]
    [AlwaysInterleave]
    [ResponseTimeout(NeuronCallTimeouts.LongRunning)]
    Task<DeliveryAdmission> Deliver(SignalDelivery delivery, CancellationToken cancellationToken = default);

    [Alias(nameof(CancelReaction))]
    [AlwaysInterleave]
    Task CancelReaction(SignalId pending);

    [ReadOnly, Alias(nameof(ReadState))]
    Task<IReadOnlyList<SignalDelivery>> ReadState();

    [ReadOnly, AlwaysInterleave, Alias(nameof(ReadPendingCount))]
    Task<int> ReadPendingCount();

    [ReadOnly, AlwaysInterleave, Alias(nameof(ReadJournal))]
    [ResponseTimeout(NeuronCallTimeouts.LongRunning)]
    Task<JournalRead> ReadJournal(JournalKind kind, long afterSequence);
}
