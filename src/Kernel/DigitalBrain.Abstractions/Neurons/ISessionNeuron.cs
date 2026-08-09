namespace DigitalBrain.Abstractions;

[ClientEntryPoint]
[Alias("DigitalBrain.Abstractions.ISessionNeuron")]
public interface ISessionNeuron : INeuron
{
    const string GrainTypeName = "sessionneuron";
    const string InstanceName = "session";

    static NeuronId ForOwner(OwnerId owner)
        => new(GrainTypeName, owner, InstanceName);

    [Alias(nameof(Fire))]
    Task<SynapseDelivery> Fire(NeuronId receiver, Synapse synapse);

    [Alias(nameof(Emit))]
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
