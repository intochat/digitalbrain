namespace DigitalBrain.Abstractions;

[Alias("db.session")]
[ClientEntryPoint]
public partial interface ISessionNeuron : INeuron
{
    const string GrainTypeName = "sessionneuron";

    [Alias(nameof(Fire))]
    [System.Diagnostics.CodeAnalysis.SuppressMessage(
        "Design",
        "CA1030:Use events where appropriate",
        Justification = "Fire is the contract's ratified verb for sending a synapse into the brain; it raises no event.")]
    Task Fire(NeuronId receiver, Synapse synapse);

    [Alias(nameof(Emit))]
    Task Emit(Synapse synapse);

    [Alias(nameof(ReadNeuronJournal))]
    Task<JournalRead> ReadNeuronJournal(NeuronId subject, JournalKind kind, long afterSequence);

    [Alias(nameof(WatchNeuron))]
    Task WatchNeuron(NeuronId subject, JournalKind kind, long afterSequence, IJournalObserver observer);

    [Alias(nameof(UnwatchNeuron))]
    Task UnwatchNeuron(NeuronId subject, IJournalObserver observer);
}
