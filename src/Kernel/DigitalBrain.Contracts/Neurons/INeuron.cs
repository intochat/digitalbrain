using DigitalBrain.Abstractions.Identity;
using DigitalBrain.Abstractions.Journals;
using DigitalBrain.Abstractions.Signals;
using DigitalBrain.Abstractions.Synapses;
using Orleans.Concurrency;

namespace DigitalBrain.Abstractions.Neurons;

// The whole surface of a neuron: the two verbs, Deliver (which only another neuron's Fire
// calls), and the reads. A read is a query — nothing moves, nothing is journaled — so the
// Read* methods interleave; Deliver interleaves too, because accepting a signal only
// appends to the journal and never runs the reaction.
[Alias("db.v3.neuron")]
public interface INeuron : IGrainWithStringKey
{
    // to == null: along every synapse of signal.Type. to != null: along exactly that synapse,
    // creating it first if missing. Returns the envelope it minted and the number of neurons
    // delivered to.
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
    Task Deliver(SignalDelivery delivery, CancellationToken cancellationToken = default);

    [ReadOnly]
    [AlwaysInterleave]
    [Alias(nameof(ReadState))]
    Task<IReadOnlyList<SignalDelivery>> ReadState();

    [ReadOnly]
    [AlwaysInterleave]
    [Alias(nameof(ReadSynapses))]
    Task<IReadOnlyList<Synapse>> ReadSynapses();

    [ReadOnly]
    [AlwaysInterleave]
    [Alias(nameof(ReadJournal))]
    [ResponseTimeout(NeuronCallTimeouts.LongRunning)]
    Task<JournalRead> ReadJournal(JournalKind kind, long afterSequence);
}
