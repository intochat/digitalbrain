using DigitalBrain.Abstractions.Identity;
using DigitalBrain.Abstractions.Journals;
using DigitalBrain.Abstractions.Signals;
using Orleans.Concurrency;

namespace DigitalBrain.Abstractions.Neurons;

[Alias("db.v3.neuron")]
public interface INeuron : IGrainWithStringKey
{
    [Alias(nameof(SendSignal))]
    [ResponseTimeout(NeuronCallTimeouts.LongRunning)]
    Task<SignalOutcome> SendSignal(Signal signal, CorrelationId? correlation = null, CancellationToken cancellationToken = default);

    [Alias(nameof(HandleSignal))]
    [AlwaysInterleave]
    [ResponseTimeout(NeuronCallTimeouts.LongRunning)]
    Task<SignalAdmission> HandleSignal(SignalDelivery delivery, CancellationToken cancellationToken = default);

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
