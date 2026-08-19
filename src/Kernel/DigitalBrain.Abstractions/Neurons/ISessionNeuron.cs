namespace DigitalBrain.Abstractions.Neurons;

[ClientEntryPoint]
[Alias("DigitalBrain.Abstractions.ISessionNeuron")]
public interface ISessionNeuron : INeuron
{
    const string GrainTypeName = "sessionneuron";
    const string InstanceName = "session";

    static NeuronId ForOwner(OwnerId owner)
        => new(GrainTypeName, owner, InstanceName);

    // A fire is a direct awaited call spanning the receiver's whole turn, so it carries
    // the same budget as the receiver's Deliver.
    [Alias(nameof(Fire))]
    [ResponseTimeout(NeuronCallTimeouts.LongRunning)]
    Task<SynapseDelivery> Fire(NeuronId receiver, Synapse synapse);

    [Alias(nameof(Emit))]
    [ResponseTimeout(NeuronCallTimeouts.LongRunning)]
    Task Emit(Synapse synapse);

    [Alias(nameof(ReadNeuronJournal))]
    [ResponseTimeout(NeuronCallTimeouts.LongRunning)]
    Task<JournalRead> ReadNeuronJournal(NeuronId subject, JournalKind kind, long afterSequence);

    [Alias(nameof(WatchNeuron))]
    [ResponseTimeout(NeuronCallTimeouts.LongRunning)]
    Task WatchNeuron(NeuronId subject, JournalKind kind, long afterSequence, IJournalObserver observer);

    [Alias(nameof(UnwatchNeuron))]
    Task UnwatchNeuron(NeuronId subject, IJournalObserver observer);
}
