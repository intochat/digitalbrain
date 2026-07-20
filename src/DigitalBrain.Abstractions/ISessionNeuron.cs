namespace DigitalBrain.Abstractions;

[Alias("db.session")]
[ClientEntryPoint]
public interface ISessionNeuron : INeuron
{
    const string GrainTypeName = "sessionneuron";

    [Alias("Fire")]
    [System.Diagnostics.CodeAnalysis.SuppressMessage(
        "Design",
        "CA1030:Use events where appropriate",
        Justification = "Fire is the contract's ratified verb for sending a synapse into the brain; it raises no event.")]
    Task FireAsync(NeuronId receiver, Synapse synapse);

    [Alias("Emit")]
    Task EmitAsync(Synapse synapse);

    [Alias("ReadNeuronJournal")]
    Task<JournalRead> ReadNeuronJournalAsync(NeuronId subject, JournalKind kind, long afterSequence);

    [Alias("WatchNeuron")]
    Task WatchNeuronAsync(NeuronId subject, JournalKind kind, long afterSequence, IJournalObserver observer);

    [Alias("UnwatchNeuron")]
    Task UnwatchNeuronAsync(NeuronId subject, IJournalObserver observer);
}
