using DigitalBrain.Abstractions.Identity;
using DigitalBrain.Abstractions.Journals;
using DigitalBrain.Abstractions.Signals;
using Orleans.Concurrency;

namespace DigitalBrain.Abstractions.Neurons;

// Object grain: emit a fact, receive a fact, read journals. Synapses live on IScenario.
[Alias("db.v3.neuron")]
public interface INeuron : IGrainWithStringKey
{
    // Journals an outgoing signal. Does not walk a friend list. `to` is rejected: bind a synapse
    // on a scenario and Route, or Deliver to a neuron you already know.
    [Alias(nameof(Fire))]
    [ResponseTimeout(NeuronCallTimeouts.LongRunning)]
    Task<FireOutcome> Fire(Signal signal, NeuronId? to, CorrelationId? correlation, CancellationToken cancellationToken = default);

    [Alias(nameof(Deliver))]
    [AlwaysInterleave]
    [ResponseTimeout(NeuronCallTimeouts.LongRunning)]
    Task<DeliveryAdmission> Deliver(SignalDelivery delivery, CancellationToken cancellationToken = default);

    [Alias(nameof(CancelReaction))]
    [AlwaysInterleave]
    Task CancelReaction(SignalId pending);

    [ReadOnly]
    [Alias(nameof(ReadState))]
    Task<IReadOnlyList<SignalDelivery>> ReadState();

    [ReadOnly]
    [AlwaysInterleave]
    [Alias(nameof(ReadPendingCount))]
    Task<int> ReadPendingCount();

    [ReadOnly]
    [AlwaysInterleave]
    [Alias(nameof(ReadJournal))]
    [ResponseTimeout(NeuronCallTimeouts.LongRunning)]
    Task<JournalRead> ReadJournal(JournalKind kind, long afterSequence);
}
