using Orleans;

namespace DigitalBrain;

[Alias("db.session")]
public interface ISessionNeuron : INeuron
{
    const string GrainTypeName = "sessionneuron";

    [Alias("Fire")]
    [System.Diagnostics.CodeAnalysis.SuppressMessage(
        "Design",
        "CA1030:Use events where appropriate",
        Justification = "Fire is the contract's ratified verb for sending a synapse into the brain; it raises no event.")]
    Task FireAsync(NeuronId receiver, Synapse synapse);
}
