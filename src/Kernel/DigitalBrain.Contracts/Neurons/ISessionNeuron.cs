using DigitalBrain.Abstractions.Identity;
using DigitalBrain.Abstractions.Signals;
using DigitalBrain.Abstractions.Journals;
using DigitalBrain.Abstractions.Synapses;
namespace DigitalBrain.Abstractions.Neurons;

[Alias("DigitalBrain.Abstractions.ISessionNeuron")]
public interface ISessionNeuron : INeuron
{
    const string GrainTypeName = "sessionneuron";
    const string InstanceName = "session";

    static NeuronId ForOwner(OwnerId owner)
        => new(GrainTypeName, owner, InstanceName);

    [Alias(nameof(Activate))]
    Task Activate();

    // A send is a direct awaited call spanning the receiver's whole turn, so it carries
    // the same budget as the receiver's Deliver.
    [Alias(nameof(Send))]
    [ResponseTimeout(NeuronCallTimeouts.LongRunning)]
    Task<SignalDeliveryResult> Send(NeuronId receiver, Signal signal);

    [Alias(nameof(ReadNeuronJournal))]
    [ResponseTimeout(NeuronCallTimeouts.LongRunning)]
    Task<JournalRead> ReadNeuronJournal(NeuronId subject, JournalKind kind, long afterSequence);

    [Alias(nameof(ReadNeuronSynapses))]
    [ResponseTimeout(NeuronCallTimeouts.LongRunning)]
    Task<IReadOnlyList<Synapse>> ReadNeuronSynapses(NeuronId subject);

    [Alias(nameof(WatchNeuron))]
    [ResponseTimeout(NeuronCallTimeouts.LongRunning)]
    Task WatchNeuron(NeuronId subject, JournalKind kind, long afterSequence, IJournalObserver observer);

    [Alias(nameof(UnwatchNeuron))]
    Task UnwatchNeuron(NeuronId subject, IJournalObserver observer);
}
