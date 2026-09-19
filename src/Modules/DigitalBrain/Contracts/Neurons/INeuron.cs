using DigitalBrain.Abstractions.Identity;
using DigitalBrain.Abstractions.Journals;
using DigitalBrain.Abstractions.Signals;
using Orleans.Concurrency;

namespace DigitalBrain.Abstractions.Neurons;

// Observation of a named object. Verbs live on IGmail, IPlaywright, etc.
// Facts enter through INeuronInbox, not here.
[Alias("db.v3.neuron")]
public interface INeuron : IGrainWithStringKey
{
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
