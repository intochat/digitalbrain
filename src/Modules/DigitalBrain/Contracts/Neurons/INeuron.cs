using DigitalBrain.Abstractions.Identity;
using DigitalBrain.Abstractions.Journals;
using DigitalBrain.Abstractions.Signals;
using Orleans.Concurrency;

namespace DigitalBrain.Abstractions.Neurons;

// The object others talk TO. Sending is not a grain call: the neuron broadcasts from inside
// a reaction (protected SendSignal). Inbound facts arrive here.
[Alias("db.v3.neuron")]
public interface INeuron : IGrainWithStringKey
{
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
