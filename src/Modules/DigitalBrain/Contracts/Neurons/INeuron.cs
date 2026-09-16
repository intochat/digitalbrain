using DigitalBrain.Abstractions.Commands;
using DigitalBrain.Abstractions.Identity;
using DigitalBrain.Abstractions.Journals;
using DigitalBrain.Abstractions.Signals;
using DigitalBrain.Abstractions.Synapses;
using Orleans.Concurrency;

namespace DigitalBrain.Abstractions.Neurons;

// The whole surface of a neuron: the two verbs, Deliver (which only another neuron's Fire
// calls), CancelReaction, and the reads. Everything that interleaves does so because it must
// reach a neuron whose turn is busy running a reaction, and none of it runs one.
[Alias("db.v3.neuron")]
public interface INeuron : IGrainWithStringKey
{
    // to == null: along every synapse of signal.Type. to != null: along exactly that synapse,
    // creating it first if missing. Returns the envelope it minted and the number of neurons
    // accepting it or reporting Busy.
    [Alias(nameof(Fire))]
    [ResponseTimeout(NeuronCallTimeouts.LongRunning)]
    Task<FireOutcome> Fire(Signal signal, NeuronId? to, CorrelationId? correlation, CancellationToken cancellationToken = default);

    [Alias(nameof(Connect))]
    Task Connect(NeuronId target, string signalType);

    [Alias(nameof(Disconnect))]
    Task Disconnect(NeuronId target, string signalType);

    // Interleaves: Deliver stages the journal append and the latest-per-type update
    // synchronously and awaits only its own state write, so a reaction firing back at
    // its source cannot block the source's next Fire.
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

    // Fire reports Busy to its emitter, and that is only actionable if the backlog behind it
    // is visible. A neuron with a full queue is by definition mid-reaction, so this read has
    // to interleave to answer at all.
    [ReadOnly]
    [AlwaysInterleave]
    [Alias(nameof(ReadPendingCount))]
    Task<int> ReadPendingCount();

    [ReadOnly]
    [Alias(nameof(ReadSynapses))]
    Task<IReadOnlyList<Synapse>> ReadSynapses();

    [ReadOnly]
    [AlwaysInterleave]
    [Alias(nameof(ReadJournal))]
    [ResponseTimeout(NeuronCallTimeouts.LongRunning)]
    Task<JournalRead> ReadJournal(JournalKind kind, long afterSequence);

    [ReadOnly]
    [AlwaysInterleave]
    [Alias(nameof(ReadCommands))]
    Task<CommandJournalRead> ReadCommands(long afterSequence);
}
